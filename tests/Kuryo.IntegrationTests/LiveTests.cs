using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kuryo.IntegrationTests.Support;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Kuryo.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class LiveTests(ApiFactory factory)
{
    [Fact]
    public async Task Dispatchers_watch_orders_move_and_couriers_drive()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.CourierAsync(factory);
        await using var ops = await LiveClient.LiveAsync(factory, firm.Owner, firm.Id);

        var order = await firm.OrderAsync();
        await ops.WaitForAsync("OrderChanged", o => o.Str("code") == order.Code && o.Str("status") == "Searching");
        await Ping(firm, ali, 41.2900, 36.3300);
        var moved = await ops.WaitForAsync("CourierChanged", c => c.Str("userId") == ali.Id.ToString() && c.GetProperty("latitude").ValueKind == JsonValueKind.Number);
        (await firm.AssignAsync(order.Id, ali)).EnsureSuccessStatusCode();
        var assigned = await ops.WaitForAsync("OrderChanged", o => o.Str("status") == "Assigned");

        Assert.Equal(41.29, moved.GetProperty("latitude").GetDouble());
        Assert.Equal("Kurye Ali", assigned.Str("courierName"));
        await ops.WaitForAsync("CourierChanged", c => c.Str("status") == "Busy");
    }

    [Fact]
    public async Task A_courier_hears_their_offer_arrive_and_go()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.CourierAsync(factory);
        await Ping(firm, ali, 41.2900, 36.3300);
        await using var phone = await LiveClient.LiveAsync(factory, ali, firm.Id);
        var order = await firm.OrderAsync();

        await factory.MatchAsync();
        var offer = await phone.WaitForAsync("OfferChanged", o => !o.IsNull());
        await ali.PostAsync($"{firm.Path}/offers/{offer.Str("id")}/decline");

        Assert.Equal(order.Code, offer.Str("orderCode"));
        await phone.WaitForAsync("OfferChanged", o => o.IsNull());
        Assert.False(await phone.HeardAsync("OrderChanged", o => o.Str("code") == order.Code), "a courier hears only orders they carry");
    }

    [Fact]
    public async Task Merchants_hear_only_their_own_orders()
    {
        var firm = await factory.FirmAsync();
        var other = await TestApi.MerchantAsync(firm.Owner, firm.Path, "Kebapçı Halil", 41.29, 36.34);
        using var cashier = await factory.RegisterAsync("Kasiyer");
        await firm.AddMemberAsync(cashier, "Merchant", firm.MerchantId);
        await using var till = await LiveClient.LiveAsync(factory, cashier, firm.Id);

        var theirs = await firm.OrderAsync(body: TestApi.OrderBody(other.Id));
        var ours = await firm.OrderAsync(cashier);

        await till.WaitForAsync("OrderChanged", o => o.Str("code") == ours.Code);
        Assert.False(await till.HeardAsync("OrderChanged", o => o.Str("code") == theirs.Code));
    }

    [Fact]
    public async Task Outsiders_cannot_watch_a_company_and_the_hub_needs_a_token()
    {
        var firm = await factory.FirmAsync();
        using var stranger = await factory.RegisterAsync("Yabancı");
        await using var connection = LiveClient.Build(factory, "/hubs/live", stranger.Token);
        await connection.StartAsync();
        using var anonymous = factory.CreateClient();

        var watch = await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync<JsonElement>("Watch", firm.Id));
        var noToken = await anonymous.PostAsync("/hubs/live/negotiate?negotiateVersion=1", null);
        var queryToken = await anonymous.PostAsync($"/hubs/live/negotiate?negotiateVersion=1&access_token={stranger.Token}", null);

        Assert.Contains("no company with this id", watch.Message);
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, queryToken.StatusCode);
    }

    [Fact]
    public async Task The_tracking_link_shows_the_delivery_without_the_customer()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.CourierAsync(factory, "Ali Veli Demir");
        await Ping(firm, ali, 41.2900, 36.3300);
        var order = await firm.OrderAsync();
        using var anonymous = factory.CreateClient();
        var path = $"/api/track/{order.TrackingToken}";

        var waiting = await anonymous.GetAsync(path);
        var before = await waiting.ReadAsync<JsonElement>();
        (await firm.AssignAsync(order.Id, ali)).EnsureSuccessStatusCode();
        var after = await (await anonymous.GetAsync(path)).ReadAsync<JsonElement>();
        var unknown = await anonymous.GetAsync("/api/track/not-a-real-token");
        var raw = await (await anonymous.GetAsync(path)).Content.ReadAsStringAsync();

        Assert.Equal(("Searching", order.DeliveryCode, "Lezzet Pide"), (before.Str("status"), before.Str("deliveryCode"), before.Str("merchantName")));
        Assert.True(before.GetProperty("courier").IsNull());
        Assert.Equal("no-store", waiting.Headers.CacheControl?.ToString());
        Assert.Equal(("Assigned", "Ali", 41.29), (after.Str("status"), after.GetProperty("courier").Str("firstName"), after.GetProperty("courier").GetProperty("latitude").GetDouble()));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.DoesNotContain("Ayşe Kaya", raw);
        Assert.DoesNotContain("0532", raw);
        Assert.DoesNotContain("Atatürk", raw);
        Assert.DoesNotContain("Kat 3", raw);
    }

    [Fact]
    public async Task The_customer_watches_the_courier_come_and_the_link_ends_a_day_after_delivery()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.CourierAsync(factory);
        await Ping(firm, ali, 41.2900, 36.3300);
        var order = await firm.OrderAsync();
        (await firm.AssignAsync(order.Id, ali)).EnsureSuccessStatusCode();
        var (customer, first) = await LiveClient.TrackAsync(factory, order.TrackingToken!);
        await using var _ = customer;
        var orderPath = $"{firm.Path}/orders/{order.Id}";

        factory.Clock.Advance(TimeSpan.FromSeconds(5));
        await Ping(firm, ali, 41.2950, 36.3200);
        var moved = await customer.WaitForAsync("TrackChanged", v => v.GetProperty("courier").GetProperty("latitude").GetDouble() == 41.295);
        await ali.PostAsync($"{orderPath}/pickup");
        await customer.WaitForAsync("TrackChanged", v => v.Str("status") == "PickedUp");
        await ali.PostAsync($"{orderPath}/deliver", new { code = order.DeliveryCode });
        var done = await customer.WaitForAsync("TrackChanged", v => v.Str("status") == "Delivered");
        factory.Clock.Advance(TimeSpan.FromHours(25));
        using var anonymous = factory.CreateClient();
        var ended = await anonymous.GetAsync($"/api/track/{order.TrackingToken}");

        Assert.Equal("Assigned", first.Str("status"));
        Assert.Equal(36.32, moved.GetProperty("courier").GetProperty("longitude").GetDouble());
        Assert.True(done.GetProperty("deliveryCode").IsNull());
        Assert.True(done.GetProperty("courier").GetProperty("latitude").IsNull());
        Assert.Equal(HttpStatusCode.NotFound, ended.StatusCode);
        await using var late = LiveClient.Build(factory, "/hubs/track", null);
        await late.StartAsync();
        await Assert.ThrowsAsync<HubException>(() => late.InvokeAsync<JsonElement>("Follow", order.TrackingToken!));
    }

    [Fact]
    public async Task A_handed_back_order_leaves_the_courier_screen()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.CourierAsync(factory);
        var order = await firm.OrderAsync();
        (await firm.AssignAsync(order.Id, ali)).EnsureSuccessStatusCode();
        await using var phone = await LiveClient.LiveAsync(factory, ali, firm.Id);

        await firm.Owner.PostAsync($"{firm.Path}/orders/{order.Id}/release", new { reason = "Kurye arıza yaptı" });

        var gone = await phone.WaitForAsync("OrderChanged", o => o.Str("code") == order.Code);
        Assert.Equal("Searching", gone.Str("status"));
        Assert.True(gone.GetProperty("courierId").IsNull());
    }

    private static async Task Ping(Firm firm, Person courier, double latitude, double longitude)
    {
        var response = await courier.Http.PostAsJsonAsync($"{firm.Path}/couriers/me/locations", new
        {
            points = new[] { new { latitude, longitude, at = DateTimeOffset.UtcNow.AddYears(1) } },
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }
}
