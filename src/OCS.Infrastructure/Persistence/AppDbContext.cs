using Microsoft.EntityFrameworkCore;
using OCS.Domain.Entities;
using OCS.Infrastructure.Persistence.Seeds;

namespace OCS.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();

    public DbSet<EmployeeProfile> EmployeeProfiles => Set<EmployeeProfile>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<AccountRole> AccountRoles => Set<AccountRole>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Brand> Brands => Set<Brand>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();

    public DbSet<AppointmentTimeSlot> AppointmentTimeSlots => Set<AppointmentTimeSlot>();

    public DbSet<Appointment> Appointments  => Set<Appointment>();

    public DbSet<ConsignmentContract> ConsignmentContracts  => Set<ConsignmentContract>();

    public DbSet<Buyout> Buyouts => Set<Buyout>();

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        DemoDataSeed.Configure(modelBuilder);
    }
}
