using System.Text.Json;
using System.Threading.Channels;
using MealMate.Application.Common.Abstractions;
using MealMate.Persistence.DriverDelivery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MealMate.Api.Realtime;

public sealed class DriverSupportIssuePostgresListener(string connectionString, IServiceScopeFactory scopeFactory,
    ILogger<DriverSupportIssuePostgresListener> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            var channel = Channel.CreateBounded<DriverSupportIssueEvent>(256);
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(stoppingToken);
                connection.Notification += (_, notification) =>
                {
                    if (notification.Channel != PostgresDriverSupportIssueTransport.NotificationChannel) return;
                    try
                    {
                        var item = JsonSerializer.Deserialize<DriverSupportIssueEvent>(notification.Payload, JsonOptions);
                        if (item is not null && item.EventId != Guid.Empty && item.IssueId != Guid.Empty && item.RestaurantId != Guid.Empty)
                            channel.Writer.TryWrite(item);
                    }
                    catch (JsonException ex) { logger.LogWarning(ex, "Ignoring malformed driver support issue event."); }
                };
                await using (var listen = new NpgsqlCommand($"LISTEN {PostgresDriverSupportIssueTransport.NotificationChannel};", connection))
                    await listen.ExecuteNonQueryAsync(stoppingToken);
                delay = TimeSpan.FromSeconds(1);
                while (!stoppingToken.IsCancellationRequested)
                {
                    await connection.WaitAsync(stoppingToken);
                    while (channel.Reader.TryRead(out var item))
                    {
                        await using var scope = scopeFactory.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<SignalRDriverSupportHandoverNotifier>()
                            .NotifyIssueAsync(item, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Driver support issue realtime listener disconnected; reconnecting in {DelaySeconds} seconds.", delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }
}
