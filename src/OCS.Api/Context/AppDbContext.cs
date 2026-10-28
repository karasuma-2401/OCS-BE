using Microsoft.EntityFrameworkCore;
using OCS.Api.Entities;
using OCS.Api.Context.Seeds;

namespace OCS.Api.Context;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();

    public DbSet<EmployeeProfile> EmployeeProfiles => Set<EmployeeProfile>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<AccountRole> AccountRoles => Set<AccountRole>();

    // AUTH TODO: Add DbSets for AuthSession, RefreshCredential, OtpChallenge, RegistrationGrant,
    // IdentityRecoveryRequest and AuthorizationGroupDelegation only after their Domain entities and
    // IEntityTypeConfiguration classes exist. Do not use this DbContext directly from controllers.
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Brand> Brands => Set<Brand>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();

    public DbSet<AppointmentTimeSlot> AppointmentTimeSlots => Set<AppointmentTimeSlot>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    public DbSet<ConsignmentContract> ConsignmentContracts => Set<ConsignmentContract>();

    public DbSet<Buyout> Buyouts => Set<Buyout>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    public DbSet<ProductStatusHistory> ProductStatusHistories => Set<ProductStatusHistory>();

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ReferenceDataSeed.Configure(modelBuilder);
    }
}
