using Kuryo.Core.Companies;
using Kuryo.Core.Merchants;
using Kuryo.Core.Orders;
using Kuryo.Core.Zones;

namespace Kuryo.UnitTests;

internal static class Builders
{
    public static readonly DateTimeOffset Now = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    public static readonly Guid Courier = Guid.NewGuid();

    public static readonly Guid Dispatcher = Guid.NewGuid();

    public static readonly Guid CompanyId = Company.Create("Hızlı Kurye", Dispatcher, Now).Company.Id;

    // Central Samsun, roughly from Atakum's edge to Canik.
    public static readonly double[][] Centre = [[41.25, 36.25], [41.25, 36.40], [41.36, 36.40], [41.36, 36.25]];

    public static Zone Zone(decimal fee = 45) => Kuryo.Core.Zones.Zone.Create(CompanyId, "Merkez", Centre, fee, Now);

    public static Merchant Merchant(int prep = 15) => Kuryo.Core.Merchants.Merchant.Create(CompanyId, "Lezzet Pide", "0362 000 00 00", "İstiklal Cd. 10", 41.2867, 36.33, prep, Now);

    public static Order Order(Merchant? merchant = null) =>
        Kuryo.Core.Orders.Order.Create(CompanyId, 1001, merchant ?? Merchant(), Zone(), "Ayşe Kaya", "0532 000 00 00", "Atatürk Blv. 50, Atakum", 41.3300, 36.2900, "Kat 3", "2 pide", Dispatcher, Now);

    /// <summary>An order a courier has picked up.</summary>
    public static Order PickedUp()
    {
        var order = Order();
        order.Assign(Courier, 24, 1.2, Now.AddMinutes(1));
        order.PickUp(Courier, 24, Now.AddMinutes(16));
        return order;
    }
}
