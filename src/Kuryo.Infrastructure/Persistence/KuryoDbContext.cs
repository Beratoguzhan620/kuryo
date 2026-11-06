using Kuryo.Core.Companies;
using Kuryo.Core.Couriers;
using Kuryo.Core.Merchants;
using Kuryo.Core.Orders;
using Kuryo.Core.Users;
using Kuryo.Core.Zones;
using Microsoft.EntityFrameworkCore;

namespace Kuryo.Infrastructure.Persistence;

/// <summary>Accounts, courier companies and their teams, merchants, delivery zones, couriers and orders.</summary>
public sealed class KuryoDbContext(DbContextOptions<KuryoDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<Merchant> Merchants => Set<Merchant>();

    public DbSet<Zone> Zones => Set<Zone>();

    public DbSet<Courier> Couriers => Set<Courier>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderEvent> OrderEvents => Set<OrderEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KuryoDbContext).Assembly);
    }
}
