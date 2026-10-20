using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace MealMate.Persistence.DriverDelivery;

public sealed class PostgresDriverSupportIssueTransport(string connectionString) : IDriverSupportIssueNotifier
{
    public const string NotificationChannel = "driver_support_issue_v1";

    public async Task PublishAsync(DriverSupportIssueEvent message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT pg_notify(@channel, @payload);", connection);
        command.Parameters.Add(new NpgsqlParameter("channel", NpgsqlDbType.Text) { Value = NotificationChannel });
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Text) { Value = payload });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
