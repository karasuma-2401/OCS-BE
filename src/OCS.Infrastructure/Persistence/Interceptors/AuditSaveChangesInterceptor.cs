using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OCS.Application.Abstractions.Auditing;
using OCS.Domain.Entities;
using OCS.Domain.Enums;

namespace OCS.Infrastructure.Persistence.Interceptors;

/// <summary>Adds redacted audit logs to the same save operation as tracked business changes.</summary>
public sealed class AuditSaveChangesInterceptor(IAuditContext auditContext) : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Opt in explicitly; new fields require review before entering the audit trail.
    private static readonly Dictionary<Type, string[]> AuditedProperties = new()
    {
        [typeof(Account)] = [nameof(Account.AccountType), nameof(Account.FullName), nameof(Account.PhoneNumber),
            nameof(Account.Username), nameof(Account.Email), nameof(Account.IdentityNumber), nameof(Account.PasswordHash),
            nameof(Account.Status), nameof(Account.PhoneVerifiedAt), nameof(Account.ActivatedAt), nameof(Account.CreatedByAccountId)],
        [typeof(CustomerProfile)] = [nameof(CustomerProfile.Address), nameof(CustomerProfile.BankCode),
            nameof(CustomerProfile.BankAccountNumber), nameof(CustomerProfile.BankAccountHolderName), nameof(CustomerProfile.Status)],
        [typeof(EmployeeProfile)] = [nameof(EmployeeProfile.JobTitle), nameof(EmployeeProfile.HiredOn), nameof(EmployeeProfile.Status)],
        [typeof(Role)] = [nameof(Role.Code), nameof(Role.Name), nameof(Role.Description)],
        [typeof(Permission)] = [nameof(Permission.Code), nameof(Permission.Name), nameof(Permission.Description)],
        [typeof(AccountRole)] = [nameof(AccountRole.AccountId), nameof(AccountRole.RoleId)],
        [typeof(RolePermission)] = [nameof(RolePermission.RoleId), nameof(RolePermission.PermissionId)],
        [typeof(Brand)] = [nameof(Brand.Name), nameof(Brand.Tier), nameof(Brand.DefaultCommissionRate), nameof(Brand.IsActive)],
        [typeof(Category)] = [nameof(Category.Name), nameof(Category.IsActive)],
        [typeof(StorageLocation)] = [nameof(StorageLocation.Code), nameof(StorageLocation.Name), nameof(StorageLocation.IsActive)],
        [typeof(AppointmentTimeSlot)] = [nameof(AppointmentTimeSlot.StartTime), nameof(AppointmentTimeSlot.EndTime),
            nameof(AppointmentTimeSlot.MaxCustomers), nameof(AppointmentTimeSlot.IsActive)],
        [typeof(Appointment)] = [nameof(Appointment.CustomerId), nameof(Appointment.TimeSlotId), nameof(Appointment.AppointmentDate),
            nameof(Appointment.EstimatedItemCount), nameof(Appointment.SubmissionMethod), nameof(Appointment.TrackingNumber),
            nameof(Appointment.Status)],
        [typeof(ConsignmentContract)] = [nameof(ConsignmentContract.ContractNumber), nameof(ConsignmentContract.CustomerId),
            nameof(ConsignmentContract.AppointmentId), nameof(ConsignmentContract.CreatedByEmployeeId),
            nameof(ConsignmentContract.SignedAt), nameof(ConsignmentContract.EndsAt), nameof(ConsignmentContract.ExpiryHandling),
            nameof(ConsignmentContract.Status)],
        [typeof(Buyout)] = [nameof(Buyout.BuyoutNumber), nameof(Buyout.CustomerId), nameof(Buyout.CreatedByEmployeeId),
            nameof(Buyout.ApprovedByEmployeeId), nameof(Buyout.TotalPurchasePrice), nameof(Buyout.Status)],
        [typeof(Product)] = [nameof(Product.Barcode), nameof(Product.Name), nameof(Product.Size), nameof(Product.Description),
            nameof(Product.ConditionPercentage), nameof(Product.BrandId), nameof(Product.CategoryId), nameof(Product.StorageLocationId),
            nameof(Product.SourceType), nameof(Product.ConsignmentContractId), nameof(Product.BuyoutId), nameof(Product.ListedPrice),
            nameof(Product.PurchasePrice), nameof(Product.AgreedCommissionRate), nameof(Product.AgreedBrandTier), nameof(Product.Status)],
        [typeof(ProductImage)] = [nameof(ProductImage.ProductId), nameof(ProductImage.Url), nameof(ProductImage.ImageType),
            nameof(ProductImage.SortOrder)],
        [typeof(SaleInvoice)] = [nameof(SaleInvoice.InvoiceNumber), nameof(SaleInvoice.SoldByEmployeeId),
            nameof(SaleInvoice.TotalAmount), nameof(SaleInvoice.PaymentStatus)],
        [typeof(SaleInvoiceLine)] = [nameof(SaleInvoiceLine.SaleInvoiceId), nameof(SaleInvoiceLine.ProductId),
            nameof(SaleInvoiceLine.SalePrice)],
        [typeof(Payout)] = [nameof(Payout.PayoutNumber), nameof(Payout.ConsignmentContractId),
            nameof(Payout.ApprovedByEmployeeId), nameof(Payout.TotalAmount), nameof(Payout.ProofUrl),
            nameof(Payout.BankTransactionReference), nameof(Payout.Status)],
        [typeof(PayoutDetail)] = [nameof(PayoutDetail.PayoutId), nameof(PayoutDetail.ProductId),
            nameof(PayoutDetail.IncidentReportId), nameof(PayoutDetail.Type), nameof(PayoutDetail.Description),
            nameof(PayoutDetail.Amount)],
        [typeof(FinancialTransaction)] = [nameof(FinancialTransaction.CustomerId), nameof(FinancialTransaction.Type),
            nameof(FinancialTransaction.SaleInvoiceId), nameof(FinancialTransaction.PayoutId),
            nameof(FinancialTransaction.BuyoutId), nameof(FinancialTransaction.EarlyWithdrawalId),
            nameof(FinancialTransaction.Amount), nameof(FinancialTransaction.PaymentMethod),
            nameof(FinancialTransaction.BankTransactionReference), nameof(FinancialTransaction.ProofUrl),
            nameof(FinancialTransaction.Status), nameof(FinancialTransaction.OccurredAt),
            nameof(FinancialTransaction.PerformedByEmployeeId)],
        [typeof(ExpiryProcessing)] = [nameof(ExpiryProcessing.ProcessedByEmployeeId),
            nameof(ExpiryProcessing.CollectionDeadline), nameof(ExpiryProcessing.Type), nameof(ExpiryProcessing.Status),
            nameof(ExpiryProcessing.ConfirmedAt)],
        [typeof(ExpiryProcessingItem)] = [nameof(ExpiryProcessingItem.ExpiryProcessingId), nameof(ExpiryProcessingItem.ProductId)],
        [typeof(EarlyWithdrawal)] = [nameof(EarlyWithdrawal.CustomerId), nameof(EarlyWithdrawal.ProcessedByEmployeeId),
            nameof(EarlyWithdrawal.TotalPenaltyAmount), nameof(EarlyWithdrawal.PaymentStatus)],
        [typeof(EarlyWithdrawalItem)] = [nameof(EarlyWithdrawalItem.EarlyWithdrawalId), nameof(EarlyWithdrawalItem.ProductId),
            nameof(EarlyWithdrawalItem.ListedPrice), nameof(EarlyWithdrawalItem.PenaltyAmount)],
        [typeof(IncidentReport)] = [nameof(IncidentReport.ProductId), nameof(IncidentReport.CreatedByEmployeeId),
            nameof(IncidentReport.ApprovedByEmployeeId), nameof(IncidentReport.Type),
            nameof(IncidentReport.CompensationAmount), nameof(IncidentReport.Status)],
        [typeof(SystemParameter)] = [nameof(SystemParameter.Key), nameof(SystemParameter.Value), nameof(SystemParameter.Description)],
        [typeof(Notification)] = [nameof(Notification.RecipientAccountId), nameof(Notification.SenderAccountId),
            nameof(Notification.Type), nameof(Notification.Title), nameof(Notification.Content), nameof(Notification.SentAt),
            nameof(Notification.IsRead)]
    };

    // Ownership per context lets retries replace automatic logs without touching manual logs.
    private readonly ConditionalWeakTable<DbContext, List<AuditLog>> pendingLogs = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (!result.HasResult)
        {
            PrepareAuditLogs(eventData.Context);
        }
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (!result.HasResult)
        {
            PrepareAuditLogs(eventData.Context);
        }
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        ForgetPendingLogs(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        ForgetPendingLogs(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => RemovePendingLogs(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        RemovePendingLogs(eventData.Context);
        return Task.CompletedTask;
    }

    public override void SaveChangesCanceled(DbContextEventData eventData) => RemovePendingLogs(eventData.Context);

    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        RemovePendingLogs(eventData.Context);
        return Task.CompletedTask;
    }

    private void PrepareAuditLogs(DbContext? context)
    {
        if (context is null)
        {
            return;
        }
        context.ChangeTracker.DetectChanges();
        if (context.ChangeTracker.Entries<AuditLog>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Audit logs cannot be updated or deleted through SaveChanges.");
        }
        // Services append lifecycle events explicitly; do not generate a second audit trail for them.
        if (context.ChangeTracker.Entries<ProductStatusHistory>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Product status history cannot be updated or deleted through SaveChanges.");
        }

        // Also handles exceptions thrown by a later SavingChanges interceptor.
        RemovePendingLogs(context);
        var entries = context.ChangeTracker.Entries()
            .Where(x => AuditedProperties.ContainsKey(x.Metadata.ClrType)
                && x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();
        var logs = new List<AuditLog>();
        foreach (var entry in entries)
        {
            var changes = GetChanges(entry);
            if (changes.Count == 0)
            {
                continue;
            }
            var now = DateTimeOffset.UtcNow;
            logs.Add(new AuditLog
            {
                ActorAccountId = auditContext.ActorAccountId,
                ActorType = auditContext.ActorType,
                Action = GetAction(entry),
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = GetEntityId(entry),
                Changes = JsonSerializer.Serialize(changes, JsonOptions),
                Source = auditContext.Source,
                IpAddress = auditContext.IpAddress,
                CorrelationId = auditContext.CorrelationId,
                OccurredAt = now,
                RecordedAt = now
            });
        }
        if (logs.Count == 0)
        {
            return;
        }
        foreach (var log in logs)
        {
            ValidateContext(log);
        }
        pendingLogs.Add(context, logs);
        try
        {
            context.Set<AuditLog>().AddRange(logs);
        }
        catch
        {
            RemovePendingLogs(context);
            throw;
        }
    }

    private static Dictionary<string, ValueChange> GetChanges(EntityEntry entry)
    {
        var changes = new Dictionary<string, ValueChange>(StringComparer.Ordinal);
        foreach (var name in AuditedProperties[entry.Metadata.ClrType])
        {
            var property = entry.Property(name);
            if (entry.State == EntityState.Modified
                && (!property.IsModified || Equals(property.OriginalValue, property.CurrentValue)))
            {
                continue;
            }
            changes[name] = new ValueChange(
                RedactValue(entry.Metadata.ClrType, name, entry.State == EntityState.Added ? null : property.OriginalValue),
                RedactValue(entry.Metadata.ClrType, name, entry.State == EntityState.Deleted ? null : property.CurrentValue));
        }
        return changes;
    }

    private static object? RedactValue(Type entityType, string name, object? value)
    {
        if (value is null)
        {
            return null;
        }
        if ((entityType == typeof(Account) && name is nameof(Account.PasswordHash)
                or nameof(Account.FullName) or nameof(Account.Email) or nameof(Account.IdentityNumber))
            || (entityType == typeof(CustomerProfile) && name is nameof(CustomerProfile.Address)
                or nameof(CustomerProfile.BankAccountHolderName)))
        {
            return "[REDACTED]";
        }
        if ((entityType == typeof(Account) && name == nameof(Account.PhoneNumber))
            || (entityType == typeof(CustomerProfile) && name == nameof(CustomerProfile.BankAccountNumber)))
        {
            var text = value.ToString()!;
            return text.Length > 4 ? "****" + text[^4..] : "[REDACTED]";
        }
        return value is Enum ? value.ToString() : value;
    }

    private static string GetEntityId(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey()
            ?? throw new InvalidOperationException("An audited entity must have a primary key.");
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var keyProperty in key.Properties)
        {
            var property = entry.Property(keyProperty.Name);
            var value = entry.State == EntityState.Deleted ? property.OriginalValue : property.CurrentValue;
            if (property.IsTemporary || value is null || value is Guid id && id == Guid.Empty)
            {
                throw new InvalidOperationException("Assign the audited entity's permanent key before saving.");
            }
            values[keyProperty.Name] = value;
        }
        return values.Count == 1
            ? values.Values.Single()!.ToString()!
            : JsonSerializer.Serialize(values, JsonOptions);
    }

    private static string GetAction(EntityEntry entry) => (entry.Entity, entry.State) switch
    {
        (AccountRole, EntityState.Added) => "RoleAssigned",
        (AccountRole, EntityState.Deleted) => "RoleRevoked",
        (RolePermission, EntityState.Added) => "PermissionGranted",
        (RolePermission, EntityState.Deleted) => "PermissionRevoked",
        (_, EntityState.Added) => entry.Metadata.ClrType.Name + "Created",
        (_, EntityState.Modified) => entry.Metadata.ClrType.Name + "Updated",
        (_, EntityState.Deleted) => entry.Metadata.ClrType.Name + "Deleted",
        _ => throw new InvalidOperationException("Unsupported audited entity state.")
    };

    private static void ValidateContext(AuditLog log)
    {
        if (!Enum.IsDefined(log.ActorType)
            || log.ActorType == AuditActorType.User && (log.ActorAccountId is null || log.ActorAccountId == Guid.Empty)
            || log.ActorType != AuditActorType.User && log.ActorAccountId is not null)
        {
            throw new InvalidOperationException("Audit actor type and account ID are inconsistent.");
        }
        if (string.IsNullOrWhiteSpace(log.Source) || log.Source.Length > 50
            || log.IpAddress?.Length > 45 || log.CorrelationId?.Length > 128)
        {
            throw new InvalidOperationException("Configure a valid audit context before saving audited changes.");
        }
    }

    private void RemovePendingLogs(DbContext? context)
    {
        if (context is null || !pendingLogs.TryGetValue(context, out var logs))
        {
            return;
        }
        foreach (var log in logs)
        {
            var entry = context.Entry(log);
            if (entry.State == EntityState.Added)
            {
                entry.State = EntityState.Detached;
            }
        }
        pendingLogs.Remove(context);
    }

    private void ForgetPendingLogs(DbContext? context)
    {
        if (context is not null)
        {
            pendingLogs.Remove(context);
        }
    }

    private sealed record ValueChange(object? Old, object? New);
}
