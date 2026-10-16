using System.Text.Json;
using MealMate.Application.Features.DriverDelivery.CustomerTracking;
using Npgsql;
using NpgsqlTypes;

namespace MealMate.Persistence.CustomerTracking;

/// <summary>Publishes compact, non-sensitive delivery invalidations through PostgreSQL NOTIFY.</summary>
public sealed class PostgresCustomerDeliveryChangeTransport(string connectionString)
    : ICustomerDeliveryChangeTransport
{
    public const string NotificationChannel = "customer_delivery_changed_v1";
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(CustomerDeliveryChangedV1 change, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(change, PayloadOptions);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT pg_notify(@channel, @payload);", connection);
        command.Parameters.Add(new NpgsqlParameter("channel", NpgsqlDbType.Text) { Value = NotificationChannel });
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Text) { Value = payload });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

}
