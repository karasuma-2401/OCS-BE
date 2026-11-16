using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Context;

namespace OCS.Api.Services;

internal static class BuyoutNumberGenerator
{
    public static async Task<string> GenerateAsync(AppDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        // Khoa giao dich giup hai luong lap phieu khong cap trung ma.
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(605235034148743)", ct);

        var date = now.ToOffset(TimeSpan.FromHours(7)).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var prefix = $"BO-{date}-";
        var sequence = await db.Buyouts.CountAsync(b => b.BuyoutNumber.StartsWith(prefix), ct) + 1;
        while (true)
        {
            var candidate = prefix + sequence.ToString("D4", CultureInfo.InvariantCulture);
            if (!await db.Buyouts.AnyAsync(b => b.BuyoutNumber == candidate, ct)) return candidate;
            sequence++;
        }
    }
}
