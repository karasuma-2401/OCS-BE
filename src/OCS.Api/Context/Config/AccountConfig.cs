using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class AccountConfig : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("TAIKHOAN", table =>
        {
            table.HasCheckConstraint(
                "CK_TAIKHOAN_AccountType",
                """
                "AccountType" IN ('Customer', 'Internal')
                """);

            table.HasCheckConstraint(
                "CK_TAIKHOAN_Status",
                """
                "Status" IN (
                    'PendingActivation',
                    'Active',
                    'Locked',
                    'Deactivated'
                )
                """);

            table.HasCheckConstraint(
                "CK_TAIKHOAN_InternalUsername",
                """
                "AccountType" <> 'Internal'
                OR (
                    "Username" IS NOT NULL
                    AND length(btrim("Username")) > 0
                )
                """);

            table.HasCheckConstraint(
                "CK_TAIKHOAN_ActiveInternalPassword",
                """
                "AccountType" <> 'Internal'
                OR "Status" <> 'Active'
                OR (
                    "PasswordHash" IS NOT NULL
                    AND length(btrim("PasswordHash")) > 0
                )
                """);
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.AccountType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.FullName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.PhoneNumber)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Username)
            .HasMaxLength(100);

        builder.Property(x => x.Email)
            .HasMaxLength(254);

        builder.Property(x => x.IdentityNumber)
            .HasMaxLength(20);

        builder.Property(x => x.PasswordHash)
            .HasMaxLength(512);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.PhoneVerifiedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.ActivatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(x => new
        {
            x.AccountType,
            x.PhoneNumber
        }).IsUnique();

        builder.HasIndex(x => x.Username)
            .IsUnique();

        builder.HasIndex(x => new
        {
            x.AccountType,
            x.Email
        }).IsUnique();

        builder.HasIndex(x => new
        {
            x.AccountType,
            x.IdentityNumber
        }).IsUnique();

        builder.HasOne(x => x.CreatedByAccount)
            .WithMany()
            .HasForeignKey(x => x.CreatedByAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}