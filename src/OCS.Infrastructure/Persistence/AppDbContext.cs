using Microsoft.EntityFrameworkCore;
using OCS.Domain.Entities;

namespace OCS.Infrastructure.Persistence;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<CustomerProfile> CustomerProfiles
        => Set<CustomerProfile>();

    public DbSet<EmployeeProfile> EmployeeProfiles
        => Set<EmployeeProfile>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<AccountRole> AccountRoles => Set<AccountRole>();

    public DbSet<RolePermission> RolePermissions
        => Set<RolePermission>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}