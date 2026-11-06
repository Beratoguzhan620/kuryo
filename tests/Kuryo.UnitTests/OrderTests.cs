using Kuryo.Core;
using Kuryo.Core.Merchants;
using Kuryo.Core.Orders;

namespace Kuryo.UnitTests;

public sealed class OrderTests
{
    [Fact]
    public void A_new_order_searches_with_the_zone_fee_a_tracking_token_and_a_code()
    {
        var order = Builders.Order();

        Assert.Equal(("KR-001001", OrderStatus.Searching, 4500L), (order.Code, order.Status, order.DeliveryFeeMinor));
        Assert.Equal(Builders.Now.AddMinutes(15), order.ReadyAt);
        Assert.InRange(order.DistanceKm, 5.5, 6.5);
        Assert.Equal(24, order.TrackingToken.Length);
        Assert.Matches("^[0-9]{4}$", order.DeliveryCode);
        Assert.Equal(("Kat 3", "2 pide"), (order.DropoffNotes, order.Items));
    }

    [Fact]
    public void Tracking_tokens_are_url_safe_and_different_every_time()
    {
        var tokens = Enumerable.Range(0, 50).Select(_ => Builders.Order().TrackingToken).ToList();

        Assert.Equal(50, tokens.Distinct().Count());
        Assert.All(tokens, t => Assert.Matches("^[A-Za-z0-9_-]+$", t));
    }

    [Fact]
    public void Addresses_outside_every_zone_and_paused_merchants_are_refused()
    {
        var outside = Assert.Throws<DomainException>(() => Order.Create(Builders.CompanyId, 1, Builders.Merchant(), Builders.Zone(), "Ayşe", "0532 000 00 00", "Bafra", 41.56, 35.90, null, null, Builders.Dispatcher, Builders.Now));
        var paused = Builders.Merchant();
        paused.Update(paused.Name, paused.Phone, paused.Address, 41.2867, 36.33, 15, active: false);

        Assert.Equal(ErrorCodes.OutsideZone, outside.Code);
        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => Builders.Order(paused)).Code);
    }

    [Fact]
    public void The_estimate_waits_for_the_order_when_the_courier_is_early()
    {
        var order = Builders.Order(Builders.Merchant(prep: 20));

        // 1.2 km to the pickup takes 5 minutes; the food is ready at minute 20; 6 km then takes 21 minutes.
        order.Assign(Builders.Courier, 24, 1.2, Builders.Now);

        Assert.Equal(Builders.Now.AddMinutes(20) + TimeSpan.FromMinutes(Math.Ceiling(order.DistanceKm * 1.35 / 24 * 60)), order.EstimatedDeliveryAt);
    }

    [Fact]
    public void The_estimate_follows_the_courier_when_they_are_late()
    {
        var order = Builders.Order(Builders.Merchant(prep: 0));

        order.Assign(Builders.Courier, 14, 7, Builders.Now);

        Assert.Equal(Builders.Now.AddMinutes(41) + TimeSpan.FromMinutes(Math.Ceiling(order.DistanceKm * 1.35 / 14 * 60)), order.EstimatedDeliveryAt);
    }

    [Fact]
    public void Only_the_assigned_courier_picks_up_and_delivers_with_the_customers_code()
    {
        var order = Builders.PickedUp();

        Assert.Equal(ErrorCodes.NotYourOrder, Assert.Throws<DomainException>(() => order.Deliver(Guid.NewGuid(), order.DeliveryCode, Builders.Now)).Code);
        order.Deliver(Builders.Courier, $" {order.DeliveryCode} ", Builders.Now.AddMinutes(40));

        Assert.Equal((OrderStatus.Delivered, Builders.Now.AddMinutes(40)), (order.Status, order.DeliveredAt));
        Assert.False(order.IsOpen);
    }

    [Fact]
    public void Five_wrong_codes_lock_the_delivery_until_a_dispatcher_confirms_it()
    {
        var order = Builders.PickedUp();
        var wrong = order.DeliveryCode == "0000" ? "1111" : "0000";

        for (var i = 1; i < Order.MaxCodeAttempts; i++)
        {
            var error = Assert.Throws<DomainException>(() => order.Deliver(Builders.Courier, wrong, Builders.Now));
            Assert.Equal(ErrorCodes.WrongCode, error.Code);
        }

        Assert.Equal(ErrorCodes.CodeLocked, Assert.Throws<DomainException>(() => order.Deliver(Builders.Courier, wrong, Builders.Now)).Code);
        Assert.Equal(ErrorCodes.CodeLocked, Assert.Throws<DomainException>(() => order.Deliver(Builders.Courier, order.DeliveryCode, Builders.Now)).Code);
        order.ConfirmDelivered(Builders.Now);
        Assert.Equal(OrderStatus.Delivered, order.Status);
    }

    [Fact]
    public void A_wrong_code_says_how_many_tries_are_left()
    {
        var order = Builders.PickedUp();

        var error = Assert.Throws<DomainException>(() => order.Deliver(Builders.Courier, "abcd", Builders.Now));

        Assert.Equal("That is not the code. 4 tries left.", error.Message);
    }

    [Fact]
    public void Handing_back_starts_the_search_again_with_a_fresh_count()
    {
        var order = Builders.Order();
        order.Offered(Builders.Now);
        order.Offered(Builders.Now);
        order.Assign(Builders.Courier, 24, 1, Builders.Now);

        var courier = order.Release(Builders.Now);

        Assert.Equal((Builders.Courier, OrderStatus.Searching, 0, (Guid?)null), (courier, order.Status, order.OffersMade, order.CourierId));
        Assert.Throws<DomainException>(() => order.Release(Builders.Now));
    }

    [Fact]
    public void An_order_nobody_takes_waits_for_a_dispatcher()
    {
        var order = Builders.Order();
        order.Offered(Builders.Now);

        order.GiveUp(Builders.Now);
        Assert.Throws<DomainException>(() => order.Offered(Builders.Now));
        order.SearchAgain(Builders.Now);

        Assert.Equal((OrderStatus.Searching, 0), (order.Status, order.OffersMade));
    }

    [Fact]
    public void Cancelling_frees_the_courier_and_finished_orders_stay_finished()
    {
        var order = Builders.Order();
        order.Assign(Builders.Courier, 24, 1, Builders.Now);

        Assert.Equal(Builders.Courier, order.Cancel("Müşteri vazgeçti", Builders.Now));
        Assert.Throws<DomainException>(() => order.Cancel("Tekrar", Builders.Now));
        Assert.Throws<DomainException>(() => order.PickUp(Builders.Courier, 24, Builders.Now));
    }

    [Fact]
    public void Picking_up_restarts_the_estimate_from_the_door()
    {
        var order = Builders.PickedUp();

        Assert.Equal(Builders.Now.AddMinutes(16) + TimeSpan.FromMinutes(Math.Ceiling(order.DistanceKm * 1.35 / 24 * 60)), order.EstimatedDeliveryAt);
    }

    [Theory]
    [InlineData("A", "0532 000 00 00")]
    [InlineData("Ayşe", "05")]
    public void Customers_need_a_name_and_a_phone(string name, string phone) =>
        Assert.Throws<DomainException>(() => Order.Create(Builders.CompanyId, 1, Builders.Merchant(), Builders.Zone(), name, phone, "Atatürk Blv. 50", 41.33, 36.29, null, null, Builders.Dispatcher, Builders.Now));

    [Fact]
    public void Zones_cover_their_edges_and_merchants_keep_a_sensible_prep_time()
    {
        var zone = Builders.Zone();

        Assert.True(zone.Contains(Kuryo.Core.Geo.GeoPoint.At(41.25, 36.30)));
        Assert.Equal(4, zone.Corners.Count);
        Assert.Throws<DomainException>(() => Merchant.Create(Builders.CompanyId, "X Döner", null, "Adres 1", 41.28, 36.33, 200, Builders.Now));
        Assert.Throws<DomainException>(() => Builders.Zone(fee: -1));
    }
}
