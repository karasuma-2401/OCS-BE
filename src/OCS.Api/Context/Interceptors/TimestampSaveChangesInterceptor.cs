using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OCS.Api.Entities;

namespace OCS.Api.Context.Interceptors;

/// <summary>Stamps the update time of tracked entities when their stored values change.</summary>
public sealed class TimestampSaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (!result.HasResult)
        {
            UpdateTimestamps(eventData.Context);
        }
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (!result.HasResult)
        {
            UpdateTimestamps(eventData.Context);
        }
        return ValueTask.FromResult(result);
    }

    private static void UpdateTimestamps(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            // Update(entity) can mark unchanged values as modified; do not stamp such no-op saves.
            if (entry.State == EntityState.Modified && entry.Properties.Any(property =>
                property.Metadata.Name is not nameof(BaseEntity.CreatedAt) and not nameof(BaseEntity.UpdatedAt)
                && property.IsModified && !Equals(property.OriginalValue, property.CurrentValue)))
            {
                entry.Property(x => x.UpdatedAt).CurrentValue = now;
            }
        }
    }
}
