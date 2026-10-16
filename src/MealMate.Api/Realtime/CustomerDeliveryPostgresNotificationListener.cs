using System.Text.Json;
using System.Threading.Channels;
using MealMate.Persistence.CustomerTracking;
using MealMate.Application.Features.DriverDelivery.CustomerTracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MealMate.Api.Realtime;

/// <summary>Listens for shared PostgreSQL invalidations and forwards them to this API instance's local hub.</summary>
public sealed class CustomerDeliveryPostgresNotificationListener(
    string connectionString,
    IServiceScopeFactory scopeFactory,
    ILogger<CustomerDeliveryPostgresNotificationListener> logger) : BackgroundService
{
    private const int QueueCapacity = 256;
    private static readonly JsonSerializerOptions PayloadOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly TaskCompletionSource _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Listening => _listening.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retryDelay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            var pending = Channel.CreateBounded<CustomerDeliveryChangedV1>(
                new BoundedChannelOptions(QueueCapacity)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false,
                }, dropped => logger.LogWarning(
                    "Customer delivery notification buffer reached capacity {Capacity}; dropping oldest invalidation {EventId} for subscription day {SubscriptionDayId}.",
                    QueueCapacity,
                    dropped.EventId,
                    dropped.SubscriptionDayId));

            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(stoppingToken);
                connection.Notification += (_, notification) =>
                {
                    if (!string.Equals(notification.Channel, PostgresCustomerDeliveryChangeTransport.NotificationChannel, StringComparison.Ordinal))
                        return;

                    try
                    {
                        var invalidation = JsonSerializer.Deserialize<CustomerDeliveryChangedV1>(
                            notification.Payload, PayloadOptions);
                        if (invalidation is null || invalidation.SchemaVersion != 1
                            || invalidation.EventId == Guid.Empty || invalidation.SubscriptionDayId == Guid.Empty)
                        {
                            logger.LogWarning("Ignoring malformed customer delivery PostgreSQL notification.");
                            return;
                        }

                        pending.Writer.TryWrite(invalidation);
                    }
                    catch (JsonException ex)
                    {
                        logger.LogWarning(ex, "Ignoring malformed customer delivery PostgreSQL notification.");
                    }
                };

                await using (var listen = new NpgsqlCommand($"LISTEN {PostgresCustomerDeliveryChangeTransport.NotificationChannel};", connection))
                    await listen.ExecuteNonQueryAsync(stoppingToken);

                _listening.TrySetResult();
                logger.LogInformation("Listening for customer delivery change invalidations.");
                retryDelay = TimeSpan.FromSeconds(1);
                while (!stoppingToken.IsCancellationRequested)
                {
                    await connection.WaitAsync(stoppingToken);
                    while (pending.Reader.TryRead(out var invalidation))
                    {
                        await using var scope = scopeFactory.CreateAsyncScope();
                        var notifier = scope.ServiceProvider.GetRequiredService<SignalRCustomerDeliveryChangeNotifier>();
                        await notifier.NotifyAsync(invalidation, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Customer delivery change listener disconnected; reconnecting in {RetryDelaySeconds} seconds.", retryDelay.TotalSeconds);
                try
                {
                    await Task.Delay(retryDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 30));
            }
        }
    }
}
