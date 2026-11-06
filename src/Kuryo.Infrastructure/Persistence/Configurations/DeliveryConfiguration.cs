using Kuryo.Core.Companies;
using Kuryo.Core.Couriers;
using Kuryo.Core.Merchants;
using Kuryo.Core.Orders;
using Kuryo.Core.Users;
using Kuryo.Core.Zones;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kuryo.Infrastructure.Persistence.Configurations;

// Locations are geography, so distances come out in metres on the earth, not in degrees.
internal sealed class MerchantConfiguration : IEntityTypeConfiguration<Merchant>
{
    public void Configure(EntityTypeBuilder<Merchant> builder)
    {
        builder.ToTable("merchants");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Name).HasMaxLength(Merchant.NameMaxLength);
        builder.Property(m => m.Phone).HasMaxLength(30);
        builder.Property(m => m.Address).HasMaxLength(Merchant.AddressMaxLength);
        builder.Property(m => m.Location).HasColumnType("geography (point, 4326)");
        builder.HasOne<Company>().WithMany().HasForeignKey(m => m.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => new { m.CompanyId, m.Name });
    }
}

internal sealed class ZoneConfiguration : IEntityTypeConfiguration<Zone>
{
    public void Configure(EntityTypeBuilder<Zone> builder)
    {
        builder.ToTable("zones");
        builder.HasKey(z => z.Id);
        builder.Property(z => z.Id).ValueGeneratedNever();
        builder.Ignore(z => z.Corners);
        builder.Property(z => z.Name).HasMaxLength(Zone.NameMaxLength);
        builder.Property(z => z.Area).HasColumnType("geography (polygon, 4326)");
        builder.HasOne<Company>().WithMany().HasForeignKey(z => z.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(z => z.Area).HasMethod("gist");
    }
}

internal sealed class CourierConfiguration : IEntityTypeConfiguration<Courier>
{
    public void Configure(EntityTypeBuilder<Courier> builder)
    {
        builder.ToTable("couriers");
        builder.HasKey(c => new { c.CompanyId, c.UserId });
        builder.Property(c => c.Vehicle).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.Plate).HasMaxLength(Courier.PlateMaxLength);
        builder.Property(c => c.Location).HasColumnType("geography (point, 4326)");
        builder.Property<uint>("Version").IsRowVersion();
        // Leaving the team takes the courier profile with it.
        builder.HasOne<Membership>().WithOne().HasForeignKey<Courier>(c => new { c.CompanyId, c.UserId }).OnDelete(DeleteBehavior.Cascade);
        // The nearest-courier search walks this index from the pickup outwards.
        builder.HasIndex(c => c.Location).HasMethod("gist");
        builder.HasIndex(c => new { c.CompanyId, c.Status });
        builder.HasIndex(c => c.UserId);
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public const string TrackingIndex = "ix_orders_tracking_token";

    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Ignore(o => o.Code);
        builder.Ignore(o => o.IsOpen);
        builder.Ignore(o => o.CodeLocked);
        builder.Property(o => o.PickupLocation).HasColumnType("geography (point, 4326)");
        builder.Property(o => o.DropoffLocation).HasColumnType("geography (point, 4326)");
        builder.Property(o => o.PickupAddress).HasMaxLength(Merchant.AddressMaxLength);
        builder.Property(o => o.CustomerName).HasMaxLength(Order.NameMaxLength);
        builder.Property(o => o.CustomerPhone).HasMaxLength(30);
        builder.Property(o => o.DropoffAddress).HasMaxLength(Order.AddressMaxLength);
        builder.Property(o => o.DropoffNotes).HasMaxLength(Order.NotesMaxLength);
        builder.Property(o => o.Items).HasMaxLength(Order.ItemsMaxLength);
        builder.Property(o => o.CancelReason).HasMaxLength(Order.NotesMaxLength);
        builder.Property(o => o.TrackingToken).HasMaxLength(32);
        builder.Property(o => o.DeliveryCode).HasMaxLength(4).IsFixedLength();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(12);
        // Two people changing one order at once: the second save fails instead of undoing the first.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasOne<Company>().WithMany().HasForeignKey(o => o.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Merchant>().WithMany().HasForeignKey(o => o.MerchantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Zone>().WithMany().HasForeignKey(o => o.ZoneId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(o => o.CourierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(o => o.CreatedBy).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(o => new { o.CompanyId, o.Number }).IsUnique();
        builder.HasIndex(o => o.TrackingToken).IsUnique().HasDatabaseName(TrackingIndex);
        builder.HasIndex(o => new { o.CompanyId, o.Status, o.CreatedAt });
        builder.HasIndex(o => new { o.MerchantId, o.CreatedAt });
        builder.HasIndex(o => new { o.CourierId, o.Status });
    }
}

internal sealed class OrderEventConfiguration : IEntityTypeConfiguration<OrderEvent>
{
    public void Configure(EntityTypeBuilder<OrderEvent> builder)
    {
        builder.ToTable("order_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityAlwaysColumn();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.Detail).HasMaxLength(OrderEvent.DetailMaxLength);
        builder.HasOne<Order>().WithMany().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.OrderId, e.Id });
    }
}
