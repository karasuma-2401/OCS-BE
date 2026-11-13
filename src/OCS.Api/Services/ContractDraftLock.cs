using System.Buffers.Binary;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OCS.Api.Context;

namespace OCS.Api.Services;

/// <summary>Đồng bộ yêu cầu điều khoản và tạo hợp đồng cho cùng một bản tiếp nhận.</summary>
public static class ContractDraftLock
{
    public static async Task<IDbContextTransaction?> AcquireAsync(AppDbContext db, Guid draftKey, CancellationToken cancellationToken)
    {
        if (!db.Database.IsNpgsql()) return null;
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var key = BinaryPrimitives.ReadInt64BigEndian(draftKey.ToByteArray());
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}
