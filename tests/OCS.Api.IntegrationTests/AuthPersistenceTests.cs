using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Context.Interceptors;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Services;
using OCS.Api.Repositories;

namespace OCS.Api.IntegrationTests;

// Each test creates and drops its own named database; no project connection string is read.
public sealed class AuthPersistenceTests
{
    private const string LegacyMigration = "20261023162400_SeparateDevelopmentSeed";

    [PostgresFact]
    public async Task BackfillPreservesLegacyIdentityHistoryAndDoesNotVerifyEmail()
    {
        await using var database = await TestDatabase.CreateAsync(LegacyMigration);
        var id = await database.AddLegacyAccountAsync(" Old+tag@Example.com ", "Customer", "Active");
        await using (var connection = await database.OpenAsync())
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO "KHACHHANG" ("Id", "Status") VALUES ($1, 'Active')
                """, connection);
            command.Parameters.AddWithValue(id);
            await command.ExecuteNonQueryAsync();
            command.CommandText = "INSERT INTO \"TAIKHOAN_VAITRO\" (\"AccountId\", \"RoleId\") VALUES ($1, '20000000-0000-0000-0000-000000000004')";
            await command.ExecuteNonQueryAsync();
        }
        await database.MigrateAsync();
        await using var context = database.Context();
        var account = await new AuthRepository(context).GetByEmailAsync("old+TAG@example.com");
        Assert.NotNull(account);
        Assert.Equal(id, account.Id);
        Assert.Equal(" Old+tag@Example.com ", account.Email);
        Assert.Equal("OLD+TAG@EXAMPLE.COM", account.NormalizedEmail);
        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.Null(account.EmailVerifiedAt);
        Assert.Null(account.PhoneVerifiedAt);
        Assert.Null(account.ActivatedAt);
        Assert.Equal(0, account.FailedLoginAttempts);
        Assert.Null(account.LockoutEndAt);
        Assert.NotNull(account.CustomerProfile);
        Assert.Single(account.AccountRoles);
    }

    [PostgresFact]
    public async Task CrossTypeCollisionStopsBackfillWithoutMergingOrChangingAccounts()
    {
        await using var database = await TestDatabase.CreateAsync(LegacyMigration);
        await database.AddLegacyAccountAsync("same@example.com", "Customer", "Locked");
        await database.AddLegacyAccountAsync(" SAME@EXAMPLE.COM ", "Internal", "Deactivated");
        var error = await Assert.ThrowsAsync<PostgresException>(() => database.MigrateAsync());
        Assert.Contains("1 conflict group", error.MessageText, StringComparison.Ordinal);
        Assert.DoesNotContain("same@example.com", error.MessageText, StringComparison.OrdinalIgnoreCase);
        await using var connection = await database.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM \"TAIKHOAN\"", connection);
        Assert.Equal(2L, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT count(*) FROM \"TAIKHOAN\" WHERE \"Status\" IN ('Locked', 'Deactivated')";
        Assert.Equal(2L, await command.ExecuteScalarAsync());
    }

    [PostgresFact]
    public async Task UnsupportedLegacyAddressesRemainUnverifiedAndAreNeverLookedUpAsNull()
    {
        await using var database = await TestDatabase.CreateAsync(LegacyMigration);
        await database.AddLegacyAccountAsync(null);
        await database.AddLegacyAccountAsync("invalid");
        await database.AddLegacyAccountAsync("nguyễn@example.com");
        await database.MigrateAsync();
        await using var context = database.Context();
        Assert.Equal(3, await context.Accounts.CountAsync(a => a.NormalizedEmail == null && a.EmailVerifiedAt == null));
        var repository = new AuthRepository(context);
        Assert.Null(await repository.GetByEmailAsync("invalid"));
        Assert.False(await repository.ExistsEmailAsync(""));
    }

    [PostgresFact]
    public async Task SqlBackfillAndTrackedWritesShareTheSameNormalizationPolicy()
    {
        await using var database = await TestDatabase.CreateAsync(LegacyMigration);
        string?[] emails = ["\v\fa.b+tag@example.com\t", "a..b@example.com", "Name <a@example.com>",
            "a@-example.com", "a@localhost", "a@@example.com", "a@example.com\u00a0", "İ@example.com",
            "a@xn--bcher-kva.example", new string('a', 65) + "@example.com", null];
        var ids = new List<Guid>();
        foreach (var email in emails)
        {
            ids.Add(await database.AddLegacyAccountAsync(email));
        }
        await database.MigrateAsync();
        await using var context = database.Context();
        for (var i = 0; i < emails.Length; i++)
        {
            var account = await context.Accounts.SingleAsync(a => a.Id == ids[i]);
            Assert.Equal(EmailIdentity.Normalize(emails[i]), account.NormalizedEmail);
        }
    }

    [PostgresFact]
    public async Task UniqueEmailSurvivesCompetingCrossTypeRegistrations()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var first = database.Context();
        await using var second = database.Context();
        first.Add(Account("same@example.com"));
        var internalAccount = Account(" SAME@EXAMPLE.COM ");
        internalAccount.AccountType = AccountType.Internal;
        internalAccount.Username = "internal";
        second.Add(internalAccount);
        static async Task<bool> SaveAsync(AppDbContext context)
        {
            try { await context.SaveChangesAsync(); return true; }
            catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            { return false; }
        }
        var saved = await Task.WhenAll(SaveAsync(first), SaveAsync(second));
        Assert.Single(saved, value => value);
        await using var verify = database.Context();
        Assert.Equal(1, await verify.Accounts.CountAsync());
    }

    [PostgresFact]
    public async Task EmailChangesClearOnlyEmailProofAndDatabaseRejectsInconsistentIdentity()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Context();
        var account = Account("first@example.com");
        var proof = DateTimeOffset.UtcNow;
        account.EmailVerifiedAt = proof;
        account.PhoneVerifiedAt = proof;
        context.Add(account);
        await context.SaveChangesAsync();
        account.Email = " SECOND@example.com ";
        await context.SaveChangesAsync();
        Assert.Equal("SECOND@EXAMPLE.COM", account.NormalizedEmail);
        Assert.Null(account.EmailVerifiedAt);
        Assert.Equal(proof, account.PhoneVerifiedAt);
        await using var connection = await database.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE \"Accounts\" SET \"NormalizedEmail\" = 'WRONG@EXAMPLE.COM' WHERE \"Id\" = $1", connection);
        command.Parameters.AddWithValue(account.Id);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [PostgresFact]
    public async Task XminPreventsLostAccountUpdatesAndDoubleChallengeConsumption()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var seed = database.Context();
        var account = Account("proof@example.com");
        var now = DateTimeOffset.UtcNow;
        var challenge = new OtpChallenge
        {
            Account = account,
            AccountId = account.Id,
            NormalizedEmail = "PROOF@EXAMPLE.COM",
            CodeHash = "test-code-hmac",
            ContinuationTokenHash = "test-token-hash",
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(5),
            ContinuationExpiresAt = now.AddMinutes(15),
            ResendAvailableAt = now.AddMinutes(1)
        };
        seed.Add(challenge);
        seed.Add(new AuthSession { Account = account, AccountId = account.Id, CreatedAt = now, ExpiresAt = now.AddHours(8) });
        await seed.SaveChangesAsync();
        await using var first = database.Context();
        await using var second = database.Context();
        var firstAccount = await first.Accounts.SingleAsync();
        var secondAccount = await second.Accounts.SingleAsync();
        firstAccount.FailedLoginAttempts++;
        secondAccount.FailedLoginAttempts++;
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        second.ChangeTracker.Clear();
        var firstChallenge = await first.OtpChallenges.SingleAsync();
        var secondChallenge = await second.OtpChallenges.SingleAsync();
        firstChallenge.ConsumedAt = now.AddSeconds(1);
        secondChallenge.ConsumedAt = now.AddSeconds(1);
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task AuthenticationSecretsNeverEnterAutomaticAuditPayloads()
    {
        await using var database = await TestDatabase.CreateAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new TestAuditContext())).Options;
        await using var context = new AppDbContext(options);
        var account = Account("private@example.com");
        account.PasswordHash = "private-password-hash";
        context.Add(account);
        await context.SaveChangesAsync();
        account.PasswordHash = "another-private-hash";
        await context.SaveChangesAsync();
        var logs = await context.AuditLogs.ToListAsync();
        Assert.Single(logs);
        Assert.DoesNotContain("PasswordHash", logs[0].Changes, StringComparison.Ordinal);
        Assert.DoesNotContain("private@example.com", logs[0].Changes, StringComparison.Ordinal);
        Assert.DoesNotContain("private-password-hash", logs[0].Changes, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizedEmail", logs[0].Changes, StringComparison.Ordinal);
    }

    private static Account Account(string email) => new()
    {
        FullName = "Persistence test",
        Email = email,
        PhoneNumber = Guid.NewGuid().ToString("N")[..15],
        AccountType = AccountType.Customer,
        Status = AccountStatus.PendingActivation
    };

    private sealed class TestAuditContext : IAuditContext
    {
        public Guid? ActorAccountId => null;
        public AuditActorType ActorType => AuditActorType.System;
        public string Source => "AuthPersistenceTest";
        public string? IpAddress => null;
        public string? CorrelationId => null;
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCS_TEST_POSTGRES")))
        {
            Skip = "Set OCS_TEST_POSTGRES to an isolated PostgreSQL server with CREATE DATABASE permission.";
        }
    }
}

internal sealed class TestDatabase(string adminConnectionString, string databaseName) : IAsyncDisposable
{
    public string ConnectionString { get; } = new NpgsqlConnectionStringBuilder(adminConnectionString)
    { Database = databaseName, Pooling = false }.ConnectionString;

    public AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(ConnectionString)
        .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
        .Options);

    public static async Task<TestDatabase> CreateAsync(string? migration = null)
    {
        var admin = Environment.GetEnvironmentVariable("OCS_TEST_POSTGRES")
            ?? throw new InvalidOperationException("OCS_TEST_POSTGRES is required.");
        var name = "ocs_auth_test_" + Guid.NewGuid().ToString("N");
        var database = new TestDatabase(admin, name);
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync();
        try { await database.MigrateAsync(migration); return database; }
        catch { await database.DisposeAsync(); throw; }
    }

    public async Task MigrateAsync(string? migration = null)
    {
        await using var context = Context();
        await context.GetService<IMigrator>().MigrateAsync(migration);
    }

    public async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task<Guid> AddLegacyAccountAsync(string? email, string accountType = "Customer", string status = "PendingActivation")
    {
        var id = Guid.NewGuid();
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO "TAIKHOAN" ("Id", "FullName", "PhoneNumber", "Email", "AccountType", "Username", "Status", "CreatedAt")
            VALUES ($1, 'Legacy test', $2, $3, $4, $5, $6, now());
            """, connection);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(id.ToString("N")[..15]);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = (object?)email ?? DBNull.Value });
        command.Parameters.AddWithValue(accountType);
        command.Parameters.AddWithValue("legacy_" + id.ToString("N"));
        command.Parameters.AddWithValue(status);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    public async ValueTask DisposeAsync()
    {
        // Only names generated above can reach this operation. Never drop the supplied admin DB.
        if (!databaseName.StartsWith("ocs_auth_test_", StringComparison.Ordinal)
            || !Guid.TryParseExact(databaseName[14..], "N", out _))
        {
            throw new InvalidOperationException("Refusing to drop a database outside the test namespace.");
        }
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
