using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Entities;
using OCS.Api.Models.Buyouts;

namespace OCS.Api.Services;

public sealed partial class BuyoutService
{
    private const string ApprovalConfigKey = "buyout.auto_approval";
    private static readonly JsonSerializerOptions ApprovalJson = new(JsonSerializerDefaults.Web);
    private static readonly BuyoutApprovalConfig DisabledConfig = new(false, null, null, null, null);

    public async Task<BuyoutApprovalConfig> GetApprovalConfigAsync(CancellationToken ct)
    {
        var value = await db.SystemParameters.AsNoTracking().Where(p => p.Key == ApprovalConfigKey).Select(p => p.Value).SingleOrDefaultAsync(ct);
        if (value is null) return DisabledConfig;
        try
        {
            var config = JsonSerializer.Deserialize<BuyoutApprovalConfig>(value, ApprovalJson);
            return config is null || config.Version is null || config.Version == Guid.Empty || config.UpdatedAt is null ||
                (config.Threshold.HasValue && (!Money(config.Threshold.Value) || config.Threshold.Value <= 0)) ||
                (config.Enabled && !config.Threshold.HasValue) ? DisabledConfig : config;
        }
        catch (JsonException) { return DisabledConfig; }
    }

    public async Task<Result<BuyoutApprovalConfig>> UpdateApprovalConfigAsync(Guid actor, UpdateBuyoutApprovalConfigRequest request, CancellationToken ct)
    {
        if (!await EmployeeActive(actor, ct)) return Fail<BuyoutApprovalConfig>("BUYOUT.EMPLOYEE_INVALID", "Cần tài khoản nhân viên đang làm việc.");
        if ((request.Enabled && !request.Threshold.HasValue) || (request.Threshold.HasValue && (!Money(request.Threshold.Value) || request.Threshold.Value <= 0)) || request.Reason?.Trim().Length > 1000)
            return Invalid<BuyoutApprovalConfig>("Ngưỡng phải lớn hơn 0, hợp lệ và có tối đa hai chữ số thập phân; lý do tối đa 1000 ký tự.");
        await using var transaction = await LockApprovalConfigAsync(ct);
        var current = await GetApprovalConfigAsync(ct);
        var existing = await db.SystemParameters.SingleOrDefaultAsync(p => p.Key == ApprovalConfigKey, ct);
        if (current.Version != request.ExpectedVersion || (existing is not null && current.Version is null))
            return Conflict<BuyoutApprovalConfig>("Cấu hình đã thay đổi hoặc không hợp lệ. Vui lòng tải lại.");
        var now = clock.GetUtcNow();
        var updated = new BuyoutApprovalConfig(request.Enabled, request.Threshold, Guid.NewGuid(), now, actor);
        if (existing is null)
        {
            existing = new SystemParameter { Key = ApprovalConfigKey, Description = "Buyout auto approval configuration" };
            db.SystemParameters.Add(existing);
        }
        existing.Value = JsonSerializer.Serialize(updated, ApprovalJson);
        db.AuditLogs.Add(new AuditLog
        {
            ActorAccountId = actor,
            ActorType = AuditActorType.User,
            Action = "UpdateBuyoutApprovalConfig",
            EntityType = nameof(SystemParameter),
            EntityId = ApprovalConfigKey,
            Changes = JsonSerializer.Serialize(new { oldValue = current, newValue = updated }, ApprovalJson),
            Reason = request.Reason?.Trim(),
            Source = "Buyout",
            OccurredAt = now,
            RecordedAt = now
        });
        try { await db.SaveChangesAsync(ct); if (transaction is not null) await transaction.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict<BuyoutApprovalConfig>("Cấu hình đã thay đổi. Vui lòng tải lại."); }
        return Result.Success(updated);
    }

    private async Task<IDbContextTransaction?> LockApprovalConfigAsync(CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return null;
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(605235034148742)", ct);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }
}
