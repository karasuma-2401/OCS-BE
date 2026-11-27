using Microsoft.EntityFrameworkCore;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Models.SystemConfig;

namespace OCS.Api.Services;

public sealed class AuditLogQueryService(AppDbContext db)
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public async Task<Result<PagedAuditLogsDto>> SearchAsync(AuditLogFilterRequest filter, CancellationToken ct)
    {
        if (filter.From.HasValue && filter.To.HasValue && filter.From.Value > filter.To.Value)
            return Result.Failure<PagedAuditLogsDto>(ApplicationError.Validation("AUDIT_LOGS.RANGE", "Ngày bắt đầu phải nhỏ hơn hoặc bằng ngày kết thúc."));

        AuditActorType? actorType = null;
        if (!string.IsNullOrWhiteSpace(filter.ActorType))
        {
            if (!Enum.TryParse<AuditActorType>(filter.ActorType.Trim(), ignoreCase: true, out var parsedActor))
                return Result.Failure<PagedAuditLogsDto>(ApplicationError.Validation("AUDIT_LOGS.ACTOR_TYPE", "Loại tác nhân không hợp lệ."));
            actorType = parsedActor;
        }

        var query = db.AuditLogs.AsNoTracking().AsQueryable();

        if (filter.From.HasValue)
        {
            var startUtc = new DateTimeOffset(filter.From.Value.ToDateTime(TimeOnly.MinValue), VietnamOffset).ToUniversalTime();
            query = query.Where(x => x.OccurredAt >= startUtc);
        }

        if (filter.To.HasValue)
        {
            var endUtc = new DateTimeOffset(filter.To.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), VietnamOffset).ToUniversalTime();
            query = query.Where(x => x.OccurredAt < endUtc);
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityType))
        {
            var entityType = filter.EntityType.Trim();
            query = query.Where(x => x.EntityType == entityType);
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            var action = filter.Action.Trim();
            query = action.ToUpperInvariant() switch
            {
                "CREATE" => query.Where(x => x.Action.EndsWith("Created") || x.Action.StartsWith("Create") || x.Action == "RoleAssigned" || x.Action == "PermissionGranted"),
                "UPDATE" => query.Where(x => x.Action.EndsWith("Updated") || x.Action.StartsWith("Update")),
                "DELETE" => query.Where(x => x.Action.EndsWith("Deleted") || x.Action.StartsWith("Delete") || x.Action == "RoleRevoked" || x.Action == "PermissionRevoked"),
                _ => query.Where(x => x.Action == action)
            };
        }

        if (actorType.HasValue)
        {
            query = query.Where(x => x.ActorType == actorType.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.EntityId.ToLower().Contains(term) ||
                x.Source.ToLower().Contains(term) ||
                (x.Reason != null && x.Reason.ToLower().Contains(term)) ||
                (x.ActorAccount != null && (
                    x.ActorAccount.FullName.ToLower().Contains(term) ||
                    (x.ActorAccount.Username != null && x.ActorAccount.Username.ToLower().Contains(term)))));
        }

        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.RecordedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AuditLogItemDto(
                x.Id,
                x.ActorAccountId,
                x.ActorAccount != null ? x.ActorAccount.FullName : null,
                x.ActorAccount != null ? x.ActorAccount.Username : null,
                x.ActorType.ToString(),
                x.Action,
                x.EntityType,
                x.EntityId,
                x.Changes,
                x.Reason,
                x.Source,
                x.OccurredAt))
            .ToArrayAsync(ct);

        return Result.Success(new PagedAuditLogsDto(items, page, pageSize, total));
    }
}
