using Npgsql;
using Testcontainers.PostgreSql;

namespace OCS.Infrastructure.IntegrationTests;

// Use Docker by default; a loopback test server can be supplied on developer machines.
internal sealed class TestPostgres : IAsyncDisposable
{
    private PostgreSqlContainer? container;
    private string? administrativeConnection;
    private readonly string databaseName = "ocs_test_" + Guid.NewGuid().ToString("N");
    private string connectionString = string.Empty;

    public string GetConnectionString() => connectionString;

    public async Task StartAsync()
    {
        var local = Environment.GetEnvironmentVariable("OCS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(local))
        {
            container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await container.StartAsync();
            connectionString = container.GetConnectionString();
            return;
        }

        var builder = new NpgsqlConnectionStringBuilder(local);
        if (builder.Host is not ("127.0.0.1" or "localhost" or "::1"))
        {
            throw new InvalidOperationException("OCS_TEST_POSTGRES must point to a dedicated loopback test server.");
        }

        builder.Database = "postgres";
        builder.Pooling = false;
        administrativeConnection = builder.ConnectionString;
        await using var connection = new NpgsqlConnection(administrativeConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {databaseName}", connection);
        await command.ExecuteNonQueryAsync();
        builder.Database = databaseName;
        connectionString = builder.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
        }

        if (administrativeConnection is not null)
        {
            await using var connection = new NpgsqlConnection(administrativeConnection);
            await connection.OpenAsync();
            // databaseName is generated here, never taken from a user-supplied connection string.
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
