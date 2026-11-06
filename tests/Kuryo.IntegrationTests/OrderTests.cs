using System.Net;
using System.Net.Http.Json;
using Kuryo.IntegrationTests.Support;

namespace Kuryo.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class OrderTests(ApiFactory factory)
{
    [Fact]
    public async Task A_merchant_sends_an_order_that_starts_searching_with_the_zone_fee()
    {
        var firm = await factory.FirmAsync();
        using var cashier = await factory.RegisterAsync("Kasiyer");
        await firm.AddMemberAsync(cashier, "Merchant", firm.MerchantId);

        var order = await firm.OrderAsync(cashier);

        Assert.Equal(("KR-001001", "Searching", "Merkez", 45m), (order.Code, order.Status, order.ZoneName, order.DeliveryFee));
        Assert.Equal(("Lezzet Pide", "Atatürk Blv. 50, Atakum", 41.33, 36.29), (order.Merchant.Name, order.Dropoff.Address, order.Dropoff.Latitude, order.Dropoff.Longitude));
        Assert.Equal((41.2867, 36.33), (order.Pickup.Latitude, order.Pickup.Longitude));
        Assert.InRange(order.DistanceKm, 5.5, 6.5);
        Assert.Matches("^[0-9]{4}$", order.DeliveryCode!);
        Assert.NotNull(order.TrackingToken);
        Assert.Equal("Created", Assert.Single(order.History).Kind);
    }

    [Fact]
    public async Task Numbers_count_up_without_gaps_when_orders_arrive_at_once()
    {
        var firm = await factory.FirmAsync();

        var orders = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => firm.OrderAsync()));

        Assert.Equal(Enumerable.Range(1001, 12), orders.Select(o => o.Number).Order());
    }

    [Fact]
    public async Task Addresses_outside_the_zones_are_refused()
    {
        var firm = await factory.FirmAsync();

        var response = await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/orders", TestApi.OrderBody(firm.MerchantId, 41.56, 35.90));

        Assert.Equal((HttpStatusCode.UnprocessableEntity, "outside_zone"), (response.StatusCode, await response.ProblemCodeAsync()));
    }

    [Fact]
    public async Task Merchants_only_send_and_see_their_own_orders()
    {
        var firm = await factory.FirmAsync();
        var other = await TestApi.MerchantAsync(firm.Owner, firm.Path, "Kebapçı Halil", 41.29, 36.34);
        using var cashier = await factory.RegisterAsync("Kasiyer");
        await firm.AddMemberAsync(cashier, "Merchant", firm.MerchantId);
        var theirs = await firm.OrderAsync(body: TestApi.OrderBody(other.Id));
        await firm.OrderAsync(cashier);

        var forOther = await cashier.Http.PostAsJsonAsync($"{firm.Path}/orders", TestApi.OrderBody(other.Id));
        var list = await (await cashier.Http.GetAsync($"{firm.Path}/orders")).ReadAsync<PageDto<OrderSummaryDto>>();
        var peek = await cashier.Http.GetAsync($"{firm.Path}/orders/{theirs.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, forOther.StatusCode);
        Assert.Equal(["Lezzet Pide"], list.Items.Select(o => o.MerchantName));
        Assert.Equal(HttpStatusCode.NotFound, peek.StatusCode);
    }

    [Fact]
    public async Task A_dispatcher_assigns_and_the_courier_picks_up_and_delivers_with_the_code()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.CourierAsync(factory);
        var order = await firm.OrderAsync();
        var path = $"{firm.Path}/orders/{order.Id}";

        var assigned = await (await firm.AssignAsync(order.Id, ali)).ReadAsync<OrderDto>();
        var asCourier = await (await ali.Http.GetAsync(path)).ReadAsync<OrderDto>();
        var picked = await (await ali.PostAsync($"{path}/pickup")).ReadAsync<OrderDto>();
        var delivered = await (await ali.PostAsync($"{path}/deliver", new { code = order.DeliveryCode })).ReadAsync<OrderDto>();
        var courier = (await (await firm.Owner.Http.GetAsync($"{firm.Path}/couriers")).ReadAsync<List<CourierDto>>()).Single();

        Assert.Equal(("Assigned", "Kurye Ali"), (assigned.Status, assigned.Courier!.Name));
        Assert.NotNull(assigned.EstimatedDeliveryAt);
        Assert.Null(asCourier.DeliveryCode);
        Assert.Null(asCourier.TrackingToken);
        Assert.Equal("PickedUp", picked.Status);
        Assert.Equal("Delivered", delivered.Status);
        Assert.Equal(["Created", "Assigned", "PickedUp", "Delivered"], delivered.History.Select(h => h.Kind));
        Assert.Equal(("Available", (Guid?)null), (courier.Status, courier.OrderId));
    }

    [Fact]
    public async Task Wrong_codes_count_and_lock_the_delivery_until_a_dispatcher_confirms_it()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.CourierAsync(factory);
        var order = await firm.OrderAsync();
        var path = $"{firm.Path}/orders/{order.Id}";
        (await firm.AssignAsync(order.Id, ali)).EnsureSuccessStatusCode();
        (await ali.PostAsync($"{path}/pickup")).EnsureSuccessStatusCode();
        var wrong = order.DeliveryCode == "0000" ? "1111" : "0000";

        var codes = new List<string?>();
        for (var i = 0; i < 5; i++)
        {
            codes.Add(await (await ali.PostAsync($"{path}/deliver", new { code = wrong })).ProblemCodeAsync());
        }

        var right = await ali.PostAsync($"{path}/deliver", new { code = order.DeliveryCode });
        var confirmed = await (await firm.Owner.Http.PostAsJsonAsync($"{path}/confirm-delivered", new { reason = "Müşteriyle telefonda konuşuldu" })).ReadAsync<OrderDto>();

        Assert.Equal(["wrong_code", "wrong_code", "wrong_code", "wrong_code", "code_locked"], codes);
        Assert.Equal("code_locked", await right.ProblemCodeAsync());
        Assert.Equal(("Delivered", 5), (confirmed.Status, confirmed.CodeAttempts));
        Assert.Equal(5, confirmed.History.Count(h => h.Kind == "WrongCode"));
    }

    [Fact]
    public async Task Couriers_only_act_on_their_own_orders_and_offline_couriers_get_none()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.CourierAsync(factory);
        using var veli = await firm.CourierAsync(factory, "Kurye Veli");
        using var resting = await firm.CourierAsync(factory, "Kurye Can", online: false);
        var order = await firm.OrderAsync();
        (await firm.AssignAsync(order.Id, ali)).EnsureSuccessStatusCode();

        var peek = await veli.Http.GetAsync($"{firm.Path}/orders/{order.Id}");
        var steal = await veli.PostAsync($"{firm.Path}/orders/{order.Id}/pickup");
        var offline = await firm.AssignAsync((await firm.OrderAsync()).Id, resting);
        var busy = await firm.AssignAsync((await firm.OrderAsync()).Id, ali);

        Assert.Equal(HttpStatusCode.NotFound, peek.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, steal.StatusCode);
        Assert.Equal((HttpStatusCode.Conflict, "courier_unavailable"), (offline.StatusCode, await offline.ProblemCodeAsync()));
        Assert.Equal("courier_unavailable", await busy.ProblemCodeAsync());
    }

    [Fact]
    public async Task One_courier_assigned_to_two_orders_at_once_gets_one()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.CourierAsync(factory);
        var orders = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => firm.OrderAsync()));

        var results = await Task.WhenAll(orders.Select(o => firm.AssignAsync(o.Id, ali)));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task A_courier_hands_an_order_back_and_it_searches_again()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.CourierAsync(factory);
        var order = await firm.OrderAsync();
        (await firm.AssignAsync(order.Id, ali)).EnsureSuccessStatusCode();

        var released = await (await ali.PostAsync($"{firm.Path}/orders/{order.Id}/release", new { reason = "Lastik patladı" })).ReadAsync<OrderDto>();
        var courier = await (await ali.Http.GetAsync($"{firm.Path}/couriers/me")).ReadAsync<CourierDto>();

        Assert.Equal(("Searching", (CourierRefDto?)null), (released.Status, released.Courier));
        Assert.Equal(("Released", "Lastik patladı"), (released.History[^1].Kind, released.History[^1].Detail));
        Assert.Equal("Available", courier.Status);
    }

    [Fact]
    public async Task Merchants_cancel_until_pickup_and_dispatchers_until_delivery()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.CourierAsync(factory);
        using var cashier = await factory.RegisterAsync("Kasiyer");
        await firm.AddMemberAsync(cashier, "Merchant", firm.MerchantId);
        var early = await firm.OrderAsync(cashier);
        var late = await firm.OrderAsync(cashier);
        (await firm.AssignAsync(late.Id, ali)).EnsureSuccessStatusCode();
        (await ali.PostAsync($"{firm.Path}/orders/{late.Id}/pickup")).EnsureSuccessStatusCode();

        var cancelEarly = await (await cashier.PostAsync($"{firm.Path}/orders/{early.Id}/cancel", new { reason = "Müşteri vazgeçti" })).ReadAsync<OrderDto>();
        var cancelLate = await cashier.PostAsync($"{firm.Path}/orders/{late.Id}/cancel", new { reason = "Müşteri vazgeçti" });
        var byOffice = await (await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/orders/{late.Id}/cancel", new { reason = "Adres yanlış" })).ReadAsync<OrderDto>();
        var courier = await (await ali.Http.GetAsync($"{firm.Path}/couriers/me")).ReadAsync<CourierDto>();
        var again = await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/orders/{late.Id}/cancel", new { reason = "Tekrar" });

        Assert.Equal("Cancelled", cancelEarly.Status);
        Assert.Equal(HttpStatusCode.Conflict, cancelLate.StatusCode);
        Assert.Equal(("Cancelled", "Adres yanlış"), (byOffice.Status, byOffice.CancelReason));
        Assert.Equal("Available", courier.Status);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Filters_find_orders_by_status_code_and_customer()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.CourierAsync(factory);
        var first = await firm.OrderAsync(body: TestApi.OrderBody(firm.MerchantId, customer: "Mehmet Öz"));
        var second = await firm.OrderAsync();
        (await firm.AssignAsync(second.Id, ali)).EnsureSuccessStatusCode();

        async Task<List<string>> Codes(string query) =>
            (await (await firm.Owner.Http.GetAsync($"{firm.Path}/orders?{query}")).ReadAsync<PageDto<OrderSummaryDto>>()).Items.Select(o => o.Code).ToList();

        Assert.Equal([second.Code, first.Code], await Codes(""));
        Assert.Equal([first.Code], await Codes("status=Searching"));
        Assert.Equal([second.Code], await Codes($"courierId={ali.Id}"));
        Assert.Equal([first.Code], await Codes("q=mehmet"));
        Assert.Equal([second.Code], await Codes($"q={second.Code}"));
        Assert.Equal(HttpStatusCode.BadRequest, (await firm.Owner.Http.GetAsync($"{firm.Path}/orders?status=Lost")).StatusCode);
    }
}
