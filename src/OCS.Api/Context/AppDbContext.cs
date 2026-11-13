using Microsoft.EntityFrameworkCore;
using OCS.Api.Entities;
using OCS.Api.Context.Seeds;
using OCS.Api.Common;

namespace OCS.Api.Context;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();

    public DbSet<EmployeeProfile> EmployeeProfiles => Set<EmployeeProfile>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<AccountRole> AccountRoles => Set<AccountRole>();

    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();

    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Brand> Brands => Set<Brand>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();

    public DbSet<AppointmentTimeSlot> AppointmentTimeSlots => Set<AppointmentTimeSlot>();
    public DbSet<BarcodeLabelTemplate> BarcodeLabelTemplates => Set<BarcodeLabelTemplate>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    public DbSet<ConsignmentContract> ConsignmentContracts => Set<ConsignmentContract>();

    public DbSet<ContractDocument> ContractDocuments => Set<ContractDocument>();
    public DbSet<ContractTemplate> ContractTemplates => Set<ContractTemplate>();
    public DbSet<WordDocumentTemplate> WordDocumentTemplates => Set<WordDocumentTemplate>();
    public DbSet<ContractTermsRequest> ContractTermsRequests => Set<ContractTermsRequest>();
    public DbSet<ConsignmentDocument> ConsignmentDocuments => Set<ConsignmentDocument>();

    public DbSet<Buyout> Buyouts => Set<Buyout>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    public DbSet<ProductStatusHistory> ProductStatusHistories => Set<ProductStatusHistory>();
    public DbSet<InventoryEvent> InventoryEvents => Set<InventoryEvent>();

    public DbSet<SaleInvoice> SaleInvoices => Set<SaleInvoice>();

    public DbSet<SaleInvoiceLine> SaleInvoiceLines => Set<SaleInvoiceLine>();

    public DbSet<Payout> Payouts => Set<Payout>();

    public DbSet<PayoutDetail> PayoutDetails => Set<PayoutDetail>();

    public DbSet<FinancialTransaction> FinancialTransactions => Set<FinancialTransaction>();

    public DbSet<ExpiryProcessing> ExpiryProcessings => Set<ExpiryProcessing>();

    public DbSet<ExpiryProcessingItem> ExpiryProcessingItems => Set<ExpiryProcessingItem>();

    public DbSet<EarlyWithdrawal> EarlyWithdrawals => Set<EarlyWithdrawal>();

    public DbSet<EarlyWithdrawalItem> EarlyWithdrawalItems => Set<EarlyWithdrawalItem>();

    public DbSet<IncidentReport> IncidentReports => Set<IncidentReport>();

    public DbSet<SystemParameter> SystemParameters => Set<SystemParameter>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        NormalizeAccountEmails();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        NormalizeAccountEmails();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void NormalizeAccountEmails()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<Account>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            entry.Property(x => x.NormalizedEmail).CurrentValue = EmailIdentity.Normalize(entry.Entity.Email);
            // Existing proof is bound to the original email, never carried over to a new address.
            if (entry.State == EntityState.Modified
                && EmailIdentity.Normalize(entry.Property(x => x.Email).OriginalValue) != entry.Entity.NormalizedEmail)
            {
                entry.Property(x => x.EmailVerifiedAt).CurrentValue = null;
            }
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ReferenceDataSeed.Configure(modelBuilder);
    }
}
