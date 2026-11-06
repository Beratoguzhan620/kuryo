using System.Net;
using System.Net.Http.Json;
using Kuryo.IntegrationTests.Support;

namespace Kuryo.IntegrationTests;

/// <summary>The pickup is Lezzet Pide at 41.2867, 36.33; couriers are placed around it.</summary>
[Collection(ApiCollection.Name)]
public sealed class MatchingTests(ApiFactory factory)
{
    private static readonly (double Lat, double Lng) Near = (41.2900, 36.3300);    // about 0.4 km away
    private static readonly (double Lat, double Lng) Farther = (41.3100, 36.3300); // about 2.6 km
    private static readonly (double Lat, double Lng) TooFar = (41.3500, 36.3900);  // about 8.7 km, past the 6 km radius

    [Fact]
    public async Task The_nearest_free_courier_gets_the_offer_first()
    {
        var firm = await factory.FirmAsync();
        using var far = await CourierAtAsync(firm, "Kurye Uzak", Farther);
        using var near = await CourierAtAsync(firm, "Kurye Yakın", Near);
        using var off = await firm.CourierAsync(factory, "Kurye Kapalı", online: false);
        var order = await firm.OrderAsync();

        var pass = await factory.MatchAsync();
        var offer = await OfferAsync(firm, near);
        var detail = await DetailAsync(firm, order.Id);

        Assert.True(pass.Offered >= 1);
        Assert.NotNull(offer);
        Assert.Equal((order.Id, order.Code, "Lezzet Pide", 45m), (offer.OrderId, offer.OrderCode, offer.MerchantName, offer.DeliveryFee));
        Assert.InRange(offer.ToPickupKm, 0.2, 0.6);
        Assert.Equal(offer.OfferedAt.AddSeconds(30), offer.ExpiresAt);
        Assert.Null(await OfferAsync(firm, far));
        Assert.Null(await OfferAsync(firm, off));
        Assert.Equal(("Kurye Yakın", 1), (detail.PendingOffer?.CourierName, detail.OffersMade));
        Assert.Contains(detail.History, h => h.Kind == "Offered" && h.Detail.StartsWith("Kurye Yakın, 0.4 km", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_declined_order_goes_to_the_next_courier_and_never_back()
    {
        var firm = await factory.FirmAsync();
        using var near = await CourierAtAsync(firm, "Kurye Yakın", Near);
        using var far = await CourierAtAsync(firm, "Kurye Uzak", Farther);
        var order = await firm.OrderAsync();
        await factory.MatchAsync();

        var first = await OfferAsync(firm, near);
        Assert.Equal(HttpStatusCode.NoContent, (await near.PostAsync($"{firm.Path}/offers/{first!.Id}/decline")).StatusCode);
        await factory.MatchAsync();
        var second = await OfferAsync(firm, far);
        await far.PostAsync($"{firm.Path}/offers/{second!.Id}/decline");
        await factory.MatchAsync();

        Assert.Null(await OfferAsync(firm, near));
        Assert.Null(await OfferAsync(firm, far));
        var detail = await DetailAsync(firm, order.Id);
        Assert.Equal(("Searching", 2), (detail.Status, detail.OffersMade));
        Assert.Equal(["Created", "Offered", "OfferDeclined", "Offered", "OfferDeclined"], detail.History.Select(h => h.Kind));
    }

    [Fact]
    public async Task An_unanswered_offer_runs_out_and_moves_on()
    {
        var firm = await factory.FirmAsync();
        using var near = await CourierAtAsync(firm, "Kurye Yakın", Near);
        using var far = await CourierAtAsync(firm, "Kurye Uzak", Farther);
        await firm.OrderAsync();
        await factory.MatchAsync();
        var first = await OfferAsync(firm, near);

        factory.Clock.Advance(TimeSpan.FromSeconds(31));
        var pass = await factory.MatchAsync();
        var late = await near.PostAsync($"{firm.Path}/offers/{first!.Id}/accept");

        Assert.True(pass.Expired >= 1);
        Assert.Equal((HttpStatusCode.Conflict, "offer_gone"), (late.StatusCode, await late.ProblemCodeAsync()));
        Assert.NotNull(await OfferAsync(firm, far));
        Assert.Null(await OfferAsync(firm, near));
    }

    [Fact]
    public async Task Accepting_assigns_the_order_with_an_estimate_and_keeps_the_courier_off_other_offers()
    {
        var firm = await factory.FirmAsync();
        using var near = await CourierAtAsync(firm, "Kurye Yakın", Near);
        using var far = await CourierAtAsync(firm, "Kurye Uzak", Farther);
        await firm.OrderAsync();
        await factory.MatchAsync();
        var offer = await OfferAsync(firm, near);

        var accepted = await near.PostAsync($"{firm.Path}/offers/{offer!.Id}/accept");
        var order = await accepted.ReadAsync<OrderDto>();
        await firm.OrderAsync();
        await factory.MatchAsync();

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(("Assigned", "Kurye Yakın"), (order.Status, order.Courier?.Name));
        Assert.Null(order.PendingOffer);
        Assert.NotNull(order.EstimatedDeliveryAt);
        Assert.Null(order.DeliveryCode);
        Assert.Equal("Ayşe Kaya", order.CustomerName);
        Assert.Null(await OfferAsync(firm, near));
        Assert.NotNull(await OfferAsync(firm, far));
        var couriers = await (await firm.Owner.Http.GetAsync($"{firm.Path}/couriers")).ReadAsync<List<CourierDto>>();
        Assert.Equal("Busy", couriers.Single(c => c.UserId == near.Id).Status);
    }

    [Fact]
    public async Task After_the_company_offers_run_out_a_dispatcher_takes_over()
    {
        var firm = await factory.FirmAsync();
        (await firm.Owner.Http.PutAsJsonAsync(firm.Path, new { name = "Hızlı Kurye", offerSeconds = 20, searchRadiusKm = 6, maxOffers = 1 })).EnsureSuccessStatusCode();
        using var near = await CourierAtAsync(firm, "Kurye Yakın", Near);
        var order = await firm.OrderAsync();
        await factory.MatchAsync();
        await near.PostAsync($"{firm.Path}/offers/{(await OfferAsync(firm, near))!.Id}/decline");

        await factory.MatchAsync();
        var detail = await DetailAsync(firm, order.Id);
        var again = await (await firm.Owner.PostAsync($"{firm.Path}/orders/{order.Id}/search-again")).ReadAsync<OrderDto>();

        Assert.Equal("Unassigned", detail.Status);
        Assert.Equal("No courier took it after 1 offer", detail.History.Last().Detail);
        Assert.Equal(("Searching", 0), (again.Status, again.OffersMade));
    }

    [Fact]
    public async Task With_nobody_near_the_search_ends_after_the_time_its_offers_would_take()
    {
        var firm = await factory.FirmAsync();
        (await firm.Owner.Http.PutAsJsonAsync(firm.Path, new { name = "Hızlı Kurye", offerSeconds = 10, searchRadiusKm = 6, maxOffers = 3 })).EnsureSuccessStatusCode();
        using var away = await CourierAtAsync(firm, "Kurye Uzakta", TooFar);
        using var quiet = await CourierAtAsync(firm, "Kurye Sessiz", Near, ago: TimeSpan.FromMinutes(3));
        var order = await firm.OrderAsync();

        await factory.MatchAsync();
        var waiting = await DetailAsync(firm, order.Id);
        factory.Clock.Advance(TimeSpan.FromSeconds(30));
        await factory.MatchAsync();
        var over = await DetailAsync(firm, order.Id);

        Assert.Equal(("Searching", 0), (waiting.Status, waiting.OffersMade));
        Assert.Null(await OfferAsync(firm, away));
        Assert.Null(await OfferAsync(firm, quiet));
        Assert.Equal(("Unassigned", "No courier was free nearby"), (over.Status, over.History.Last().Detail));
    }

    [Fact]
    public async Task Assigning_by_hand_or_cancelling_takes_the_offer_back()
    {
        var firm = await factory.FirmAsync();
        using var near = await CourierAtAsync(firm, "Kurye Yakın", Near);
        using var far = await CourierAtAsync(firm, "Kurye Uzak", Farther);
        var first = await firm.OrderAsync();
        await factory.MatchAsync();
        var offer = await OfferAsync(firm, near);

        var assigned = await (await firm.AssignAsync(first.Id, far)).ReadAsync<OrderDto>();
        var late = await near.PostAsync($"{firm.Path}/offers/{offer!.Id}/accept");
        var second = await firm.OrderAsync();
        await factory.MatchAsync();
        var held = await OfferAsync(firm, near);
        await firm.Owner.PostAsync($"{firm.Path}/orders/{second.Id}/cancel", new { reason = "Müşteri vazgeçti" });

        Assert.Equal(("Assigned", "Kurye Uzak"), (assigned.Status, assigned.Courier?.Name));
        Assert.Null(assigned.PendingOffer);
        Assert.Equal((HttpStatusCode.Conflict, "offer_gone"), (late.StatusCode, await late.ProblemCodeAsync()));
        Assert.Equal(second.Id, held?.OrderId);
        Assert.Null(await OfferAsync(firm, near));
    }

    [Fact]
    public async Task Going_offline_hands_the_offer_to_the_next_courier()
    {
        var firm = await factory.FirmAsync();
        using var near = await CourierAtAsync(firm, "Kurye Yakın", Near);
        using var far = await CourierAtAsync(firm, "Kurye Uzak", Farther);
        await firm.OrderAsync();
        await factory.MatchAsync();
        Assert.NotNull(await OfferAsync(firm, near));

        (await near.Http.PostAsJsonAsync($"{firm.Path}/couriers/me/status", new { online = false })).EnsureSuccessStatusCode();
        await factory.MatchAsync();

        Assert.Null(await OfferAsync(firm, near));
        Assert.NotNull(await OfferAsync(firm, far));
    }

    [Fact]
    public async Task Positions_are_shared_only_online_and_the_newest_one_counts()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.CourierAsync(factory, online: false);
        var now = factory.Clock.GetUtcNow();
        var path = $"{firm.Path}/couriers/me/locations";

        var offline = await ali.Http.PostAsJsonAsync(path, new { points = new[] { new { latitude = 41.29, longitude = 36.33, at = now } } });
        (await ali.Http.PostAsJsonAsync($"{firm.Path}/couriers/me/status", new { online = true })).EnsureSuccessStatusCode();
        var tooMany = await ali.Http.PostAsJsonAsync(path, new { points = Enumerable.Range(0, 51).Select(_ => new { latitude = 41.29, longitude = 36.33, at = now }) });
        var stored = await (await ali.Http.PostAsJsonAsync(path, new
        {
            points = new[]
            {
                new { latitude = 41.2950, longitude = 36.3310, at = now.AddMinutes(5) },
                new { latitude = 41.2900, longitude = 36.3300, at = now.AddSeconds(-20) },
                new { latitude = 41.2920, longitude = 36.3305, at = now.AddSeconds(-10) },
            },
        })).ReadAsync<PingDto>();
        var older = await (await ali.Http.PostAsJsonAsync(path, new { points = new[] { new { latitude = 41.30, longitude = 36.34, at = now.AddMinutes(-1) } } })).ReadAsync<PingDto>();
        var me = await (await ali.Http.GetAsync($"{firm.Path}/couriers/me")).ReadAsync<CourierDto>();
        var trail = await (await firm.Owner.Http.GetAsync($"{firm.Path}/couriers/{ali.Id}/trail")).ReadAsync<List<TrailPointDto>>();

        Assert.Equal((HttpStatusCode.Conflict, "invalid_state"), (offline.StatusCode, await offline.ProblemCodeAsync()));
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Equal((3, now), (stored.Stored, stored.LocationAt));
        Assert.InRange(older.LocationAt!.Value, now.AddMilliseconds(-1), now);
        Assert.Equal((41.295, 36.331), (me.Latitude, me.Longitude));
        Assert.Equal([41.30, 41.29, 41.292, 41.295], trail.Select(p => p.Latitude));
    }

    [Fact]
    public async Task The_trail_of_an_order_holds_only_the_positions_while_carrying_it()
    {
        var firm = await factory.FirmAsync();
        using var ali = await CourierAtAsync(firm, "Kurye Ali", Near);
        var order = await firm.OrderAsync();
        (await firm.AssignAsync(order.Id, ali)).EnsureSuccessStatusCode();
        factory.Clock.Advance(TimeSpan.FromSeconds(5));
        await PingAsync(firm, ali, (41.2880, 36.3300));
        factory.Clock.Advance(TimeSpan.FromSeconds(5));
        await PingAsync(firm, ali, (41.2870, 36.3300));

        var trail = await (await firm.Owner.Http.GetAsync($"{firm.Path}/couriers/{ali.Id}/trail?orderId={order.Id}")).ReadAsync<List<TrailPointDto>>();
        var byCourier = await ali.Http.GetAsync($"{firm.Path}/couriers/{ali.Id}/trail");

        Assert.Equal([41.288, 41.287], trail.Select(p => p.Latitude));
        Assert.Equal(HttpStatusCode.Forbidden, byCourier.StatusCode);
    }

    [Fact]
    public async Task Matchers_running_at_once_never_offer_an_order_or_a_courier_twice()
    {
        var firm = await factory.FirmAsync();
        var couriers = new List<Person>();
        for (var i = 0; i < 6; i++)
        {
            couriers.Add(await CourierAtAsync(firm, $"Kurye {i}", (41.2880 + i * 0.002, 36.3300)));
        }

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => firm.OrderAsync()));

        await Task.WhenAll(factory.MatchAsync(), factory.MatchAsync(), factory.MatchAsync());
        await factory.MatchAsync();

        Assert.Equal(6L, await factory.ScalarAsync<long>("SELECT count(*) FROM offers WHERE company_id = @p0 AND status = 'Pending'", firm.Id));
        Assert.Equal(6L, await factory.ScalarAsync<long>("SELECT count(DISTINCT courier_id) FROM offers WHERE company_id = @p0 AND status = 'Pending'", firm.Id));
        Assert.Equal(6L, await factory.ScalarAsync<long>("SELECT count(DISTINCT order_id) FROM offers WHERE company_id = @p0 AND status = 'Pending'", firm.Id));
        couriers.ForEach(c => c.Dispose());
    }

    private async Task<Person> CourierAtAsync(Firm firm, string name, (double Lat, double Lng) at, TimeSpan? ago = null)
    {
        var courier = await firm.CourierAsync(factory, name);
        await PingAsync(firm, courier, at, ago);
        return courier;
    }

    private async Task PingAsync(Firm firm, Person courier, (double Lat, double Lng) at, TimeSpan? ago = null)
    {
        var response = await courier.Http.PostAsJsonAsync($"{firm.Path}/couriers/me/locations", new
        {
            points = new[] { new { latitude = at.Lat, longitude = at.Lng, at = factory.Clock.GetUtcNow() - (ago ?? TimeSpan.Zero) } },
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<OfferDto?> OfferAsync(Firm firm, Person courier)
    {
        var response = await courier.Http.GetAsync($"{firm.Path}/couriers/me/offer");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return response.StatusCode == HttpStatusCode.NoContent ? null : await response.ReadAsync<OfferDto>();
    }

    private static async Task<OrderDto> DetailAsync(Firm firm, Guid orderId) =>
        await (await firm.Owner.Http.GetAsync($"{firm.Path}/orders/{orderId}")).ReadAsync<OrderDto>();
}
