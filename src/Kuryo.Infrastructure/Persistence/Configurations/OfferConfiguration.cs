using Kuryo.Core.Companies;
using Kuryo.Core.Couriers;
using Kuryo.Core.Offers;
using Kuryo.Core.Orders;
using Kuryo.Core.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kuryo.Infrastructure.Persistence.Configurations;

internal sealed class OfferConfiguration : IEntityTypeConfiguration<Offer>
{
    public const string OnePerCourier = "ix_offers_one_pending_per_courier";
    public const string OnePerOrder = "ix_offers_one_pending_per_order";

    public void Configure(EntityTypeBuilder<Offer> builder)
    {
        builder.ToTable("offers");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property<uint>("Version").IsRowVersion();
        builder.HasOne<Order>().WithMany().HasForeignKey(o => o.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(o => o.CourierId).OnDelete(DeleteBehavior.Restrict);
        // The database itself refuses a second live offer for a courier or an order, whatever the code does.
        builder.HasIndex(o => o.CourierId).IsUnique().HasFilter("status = 'Pending'").HasDatabaseName(OnePerCourier);
        builder.HasIndex(o => o.OrderId).IsUnique().HasFilter("status = 'Pending'").HasDatabaseName(OnePerOrder);
        builder.HasIndex(o => new { o.Status, o.ExpiresAt });
        builder.HasIndex(o => new { o.OrderId, o.CourierId });
    }
}

internal sealed class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.ToTable("positions");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).UseIdentityAlwaysColumn();
        builder.Property(p => p.Location).HasColumnType("geography (point, 4326)");
        builder.HasOne<Company>().WithMany().HasForeignKey(p => p.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => new { p.OrderId, p.At }).HasFilter("order_id IS NOT NULL");
        builder.HasIndex(p => new { p.CourierId, p.At });
    }
}
