using Contratos.Domain.Organizations;
using Contratos.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contratos.Infrastructure.Persistence.Configurations;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("users");
        b.Property(u => u.Id).ValueGeneratedNever();
        b.Property(u => u.Name).HasMaxLength(100).IsRequired();
        b.Property(u => u.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(u => u.CreatedAt).HasDefaultValueSql("now()");

        // v1: um e-mail pertence a uma única organização.
        b.HasIndex(u => u.NormalizedEmail).IsUnique().HasDatabaseName("ix_users_normalized_email");
        b.HasIndex(u => u.NormalizedUserName).IsUnique().HasDatabaseName("ix_users_normalized_user_name");
        b.HasIndex(u => u.OrganizationId);
        b.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(u => u.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> b) => b.ToTable("user_claims");
}

internal sealed class UserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> b) => b.ToTable("user_logins");
}

internal sealed class UserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> b) => b.ToTable("user_tokens");
}
