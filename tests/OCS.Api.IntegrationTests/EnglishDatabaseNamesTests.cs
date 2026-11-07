using Microsoft.EntityFrameworkCore;
using Npgsql;
using OCS.Api.Repositories;

namespace OCS.Api.IntegrationTests;

public sealed class EnglishDatabaseNamesTests
{
    private const string BeforeRename = "20261104100000_UpdateAppointmentStatusAndFields";
    private const string Rename = "20261104150256_UseEnglishDatabaseNames";
    private static readonly Dictionary<string, string> TableNames = new()
    {
        ["TAIKHOAN"] = "Accounts",
        ["KHACHHANG"] = "CustomerProfiles",
        ["NHANVIEN"] = "EmployeeProfiles",
        ["VAITRO"] = "Roles",
        ["QUYEN"] = "Permissions",
        ["TAIKHOAN_VAITRO"] = "AccountRoles",
        ["PHANQUYEN"] = "RolePermissions",
        ["AUTH_SESSION"] = "AuthSessions",
        ["OTP_CHALLENGE"] = "OtpChallenges",
        ["NHATKYHETHONG"] = "AuditLogs",
        ["THUONGHIEU"] = "Brands",
        ["DANHMUC"] = "Categories",
        ["KHOLUU"] = "StorageLocations",
        ["KHUNGGIOHEN"] = "AppointmentTimeSlots",
        ["DATLICH"] = "Appointments",
        ["HOPDONGKYGUI"] = "ConsignmentContracts",
        ["THUMUA"] = "Buyouts",
        ["SANPHAM"] = "Products",
        ["ANH_SANPHAM"] = "ProductImages",
        ["LICHSUTRANGTHAISP"] = "ProductStatusHistories",
        ["HOADONBAN"] = "SaleInvoices",
        ["CT_HOADON"] = "SaleInvoiceLines",
        ["PHIEUGIAINGAN"] = "Payouts",
        ["CT_GIAINGAN"] = "PayoutDetails",
        ["GIAODICHTAICHINH"] = "FinancialTransactions",
        ["PHIEUXULYHETHAN"] = "ExpiryProcessings",
        ["CT_XULY"] = "ExpiryProcessingItems",
        ["PHIEURUTHANGSOM"] = "EarlyWithdrawals",
        ["CT_RUTSOM"] = "EarlyWithdrawalItems",
        ["BIENBANSUCO"] = "IncidentReports",
        ["THAMSO"] = "SystemParameters",
        ["THONGBAO"] = "Notifications"
    };

    private static async Task<HashSet<string>> ColumnsAsync(TestDatabase database, bool useOldNames)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        await using var connection = await database.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT table_name, column_name FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory';
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var table = reader.GetString(0);
            if (useOldNames) table = TableNames[table];
            result.Add(table + "." + reader.GetString(1));
        }
        return result;
    }

    [PostgresFact]
    public async Task AppointmentMigrationCanRunAfterEnglishRename()
    {
        const string beforeFeature = "20261103101757_NormalizeAccountEmail";
        const string feature = "20261104100000_UpdateAppointmentStatusAndFields";
        await using var database = await TestDatabase.CreateAsync(beforeFeature);

        // Simulate a database renamed before the feature migration was introduced.
        await using (var connection = await database.OpenAsync())
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('20261104100000_UpdateAppointmentStatusAndFields', '10.0.12');
                """, connection);
            await command.ExecuteNonQueryAsync();
        }
        await database.MigrateAsync();
        await using (var connection = await database.OpenAsync())
        {
            await using var command = new NpgsqlCommand("DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = $1", connection);
            command.Parameters.AddWithValue(feature);
            await command.ExecuteNonQueryAsync();
        }

        await database.MigrateAsync();
        await using var verify = await database.OpenAsync();
        await using var columns = new NpgsqlCommand("""
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'Appointments'
              AND column_name IN ('Note', 'CancellationReason', 'ReminderSentAt');
            """, verify);
        Assert.Equal(3L, await columns.ExecuteScalarAsync());
        columns.CommandText = """
            SELECT count(*) FROM pg_constraint
            WHERE conrelid = '"Appointments"'::regclass AND conname = 'CK_Appointments_Enums'
              AND pg_get_constraintdef(oid) LIKE '%CheckedIn%';
            """;
        Assert.Equal(1L, await columns.ExecuteScalarAsync());
    }

    [PostgresFact]
    public async Task RenamePreservesAccountsRelationshipsConstraintsAndSupportsRollback()
    {
        await using var database = await TestDatabase.CreateAsync(BeforeRename);
        var accountId = Guid.NewGuid();
        await using (var connection = await database.OpenAsync())
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO "TAIKHOAN" ("Id", "FullName", "PhoneNumber", "Email", "NormalizedEmail", "AccountType", "Status", "CreatedAt")
                VALUES ($1, 'Existing user', '0901234567', 'existing@example.com', 'EXISTING@EXAMPLE.COM', 'Customer', 'PendingActivation', now())
                """, connection);
            command.Parameters.AddWithValue(accountId);
            await command.ExecuteNonQueryAsync();
            command.CommandText = "INSERT INTO \"KHACHHANG\" (\"Id\", \"Status\") VALUES ($1, 'Active')";
            await command.ExecuteNonQueryAsync();
            command.CommandText = "INSERT INTO \"TAIKHOAN_VAITRO\" (\"AccountId\", \"RoleId\") VALUES ($1, '20000000-0000-0000-0000-000000000004')";
            await command.ExecuteNonQueryAsync();
        }

        var expectedColumns = await ColumnsAsync(database, true);
        await database.MigrateAsync(Rename);
        var actualColumns = await ColumnsAsync(database, false);
        Assert.True(expectedColumns.SetEquals(actualColumns), "Renaming must preserve every table column.");
        await using (var context = database.Context())
        {
            var account = await new AuthRepository(context).GetByEmailAsync("existing@example.com");
            Assert.NotNull(account);
            Assert.Equal(accountId, account.Id);
            Assert.NotNull(account.CustomerProfile);
            Assert.Single(account.AccountRoles);
        }
        await using (var connection = await database.OpenAsync())
        {
            await using var command = new NpgsqlCommand("""
                SELECT count(*) FROM pg_constraint
                WHERE conrelid = '"Accounts"'::regclass AND conname = 'CK_Accounts_NormalizedEmail';
                """, connection);
            Assert.Equal(1L, await command.ExecuteScalarAsync());
            command.CommandText = "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'TAIKHOAN'";
            Assert.Equal(0L, await command.ExecuteScalarAsync());
        }

        await database.MigrateAsync(BeforeRename);
        await using (var connection = await database.OpenAsync())
        {
            await using var command = new NpgsqlCommand("""
                SELECT count(*) FROM "TAIKHOAN" a
                JOIN "KHACHHANG" c ON c."Id" = a."Id"
                JOIN "TAIKHOAN_VAITRO" ar ON ar."AccountId" = a."Id"
                WHERE a."Id" = $1;
                """, connection);
            command.Parameters.AddWithValue(accountId);
            Assert.Equal(1L, await command.ExecuteScalarAsync());
        }
        await database.MigrateAsync();
        await using var finalContext = database.Context();
        Assert.True(await finalContext.Accounts.AnyAsync(a => a.Id == accountId));
    }
}
