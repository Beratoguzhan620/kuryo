using Kuryo.Core.Companies;
using Kuryo.Core.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kuryo.Infrastructure.Persistence.Configurations;

internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("companies");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(Company.NameMaxLength);
        builder.Property(c => c.TimeZone).HasMaxLength(64);
        builder.Property(c => c.NextOrderNumber).HasDefaultValue(1001);
        builder.Property(c => c.OfferSeconds).HasDefaultValue(30);
        builder.Property(c => c.SearchRadiusKm).HasDefaultValue(6.0);
        builder.Property(c => c.MaxOffers).HasDefaultValue(5);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("memberships");
        builder.HasKey(m => new { m.CompanyId, m.UserId });
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(12);
        builder.Ignore(m => m.IsDispatcher);
        builder.HasOne<Company>().WithMany().HasForeignKey(m => m.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => m.UserId);
        builder.HasOne<Kuryo.Core.Merchants.Merchant>().WithMany().HasForeignKey(m => m.MerchantId).OnDelete(DeleteBehavior.Cascade);
    }
}
