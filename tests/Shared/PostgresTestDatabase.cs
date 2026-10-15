using Npgsql;
using Testcontainers.PostgreSql;

namespace MealMate.Testing;

/// <summary>Real PostgreSQL isolation via Docker, or a dedicated database on an explicitly configured local test server.</summary>
public sealed class PostgresTestDatabase : IAsyncDisposable
{
    private PostgreSqlContainer? _container;
    private string? _adminConnection;
    private string? _connectionString;
    private string? _databaseName;

    public async Task StartAsync()
    {
        var configured = Environment.GetEnvironmentVariable("MEALMATE_TEST_POSTGRES_ADMIN");
        if (string.IsNullOrWhiteSpace(configured) && OperatingSystem.IsWindows())
            configured = Environment.GetEnvironmentVariable("MEALMATE_TEST_POSTGRES_ADMIN", EnvironmentVariableTarget.User);
        if (string.IsNullOrWhiteSpace(configured))
        {
            _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
            await _container.StartAsync();
            _connectionString = _container.GetConnectionString();
            return;
        }

        var admin = new NpgsqlConnectionStringBuilder(configured);
        if (admin.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new InvalidOperationException("The explicit PostgreSQL test administrator connection must point to a local server.");
        // Only databases created by this fixture are migrated or dropped. Never use the configured database as the test database.
        admin.Database = "postgres";
        admin.Pooling = false;
        admin.IncludeErrorDetail = false;
        _adminConnection = admin.ConnectionString;
        var databaseName = "mealmate_test_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
        await create.ExecuteNonQueryAsync();
        _databaseName = databaseName;
        var isolated = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = _databaseName };
        _connectionString = isolated.ConnectionString;
    }

    public string GetConnectionString()
        => _connectionString ?? throw new InvalidOperationException("The PostgreSQL test database has not been started.");

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
            return;
        }
        if (_adminConnection is null || _databaseName is null) return;
        if (!_databaseName.StartsWith("mealmate_test_", StringComparison.Ordinal)
            || !Guid.TryParseExact(_databaseName[14..], "N", out _))
            throw new InvalidOperationException("Refusing cleanup of a database not created by the test fixture.");
        await using var connection = new NpgsqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
        _databaseName = null;
    }
}
