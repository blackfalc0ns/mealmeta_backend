using System.Collections.Concurrent;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Api.Realtime;

/// <summary>
/// Node-local SignalR connection bindings, not business state. Serializes rebinds
/// and removes prior device groups. Outbound routing validates the persistent
/// device session and uses SignalR groups, independent of these local bindings.
/// </summary>
public sealed class VoiceCallV2ConnectionRegistry
{
    private sealed class Connection
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public Guid UserId;
        public Guid DeviceSessionId;
    }

    private readonly ConcurrentDictionary<string, Connection> _connections = new();

    public async Task BindAsync(string connectionId, Guid userId, Guid deviceSessionId,
        Func<Guid?, Task> moveGroups)
    {
        var connection = _connections.GetOrAdd(connectionId, _ => new Connection());
        await connection.Gate.WaitAsync();
        try
        {
            await moveGroups(connection.DeviceSessionId == Guid.Empty ? null : connection.DeviceSessionId);
            connection.UserId = userId;
            connection.DeviceSessionId = deviceSessionId;
        }
        finally { connection.Gate.Release(); }
    }

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    public async Task<bool> SendAsync(IAppDbContext db, IDateTimeProvider clock, Guid deviceSessionId,
        Func<string, Task> send, CancellationToken cancellationToken)
    {
        // All group members bound with proof to this immutable session ID/owner.
        // A revoked or expired session invalidates the whole group, including
        // remote members, so never broadcast without this database check.
        var valid = await db.Set<VoiceCallDeviceSession>().AsNoTracking().AnyAsync(s =>
            s.Id == deviceSessionId && s.RevokedAtUtc == null && s.ExpiresAtUtc > clock.UtcNow,
            cancellationToken);
        if (!valid) return false;
        await send(VoiceCallV2Hub.GroupForDevice(deviceSessionId));
        // Successful lifetime-manager enqueue; not a device acknowledgement.
        return true;
    }
}
