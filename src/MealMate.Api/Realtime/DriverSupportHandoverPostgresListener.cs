using System.Text.Json;
using System.Threading.Channels;
using MealMate.Application.Common.Abstractions;
using MealMate.Persistence.DriverDelivery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MealMate.Api.Realtime;

public sealed class DriverSupportHandoverPostgresListener(string connectionString, IServiceScopeFactory scopeFactory,
    ILogger<DriverSupportHandoverPostgresListener> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            var channel = Channel.CreateBounded<DriverSupportHandoverEvent>(256);
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(stoppingToken);
                connection.Notification += (_, notification) =>
                {
                    if (notification.Channel != PostgresDriverSupportHandoverTransport.NotificationChannel) return;
                    try
                    {
                        var item = JsonSerializer.Deserialize<DriverSupportHandoverEvent>(notification.Payload, JsonOptions);
                        if (item is not null && item.EventId != Guid.Empty && item.HandoverId != Guid.Empty)
                            channel.Writer.TryWrite(item);
                    }
                    catch (JsonException ex) { logger.LogWarning(ex, "Ignoring malformed driver support handover event."); }
                };
                await using (var listen = new NpgsqlCommand($"LISTEN {PostgresDriverSupportHandoverTransport.NotificationChannel};", connection))
                    await listen.ExecuteNonQueryAsync(stoppingToken);
                delay = TimeSpan.FromSeconds(1);
                while (!stoppingToken.IsCancellationRequested)
                {
                    await connection.WaitAsync(stoppingToken);
                    while (channel.Reader.TryRead(out var item))
                    {
                        await using var scope = scopeFactory.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<SignalRDriverSupportHandoverNotifier>()
                            .NotifyAsync(item, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Driver support handover realtime listener disconnected; reconnecting in {DelaySeconds} seconds.", delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }
}
