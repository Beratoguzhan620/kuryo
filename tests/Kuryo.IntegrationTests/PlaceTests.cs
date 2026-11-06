using System.Net;
using System.Net.Http.Json;
using Kuryo.IntegrationTests.Support;

namespace Kuryo.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PlaceTests(ApiFactory factory)
{
    [Fact]
    public async Task Zones_come_back_as_drawn_with_their_size()
    {
        var firm = await factory.FirmAsync();

        var zones = await (await firm.Owner.Http.GetAsync($"{firm.Path}/zones")).ReadAsync<List<ZoneDto>>();

        var zone = Assert.Single(zones);
        Assert.Equal(("Merkez", 45m, 4), (zone.Name, zone.DeliveryFee, zone.Corners.Count));
        // About 12 by 12.6 km.
        Assert.InRange(zone.AreaKm2, 140, 165);
    }

    [Fact]
    public async Task Areas_with_crossing_edges_are_refused()
    {
        var firm = await factory.FirmAsync();

        var response = await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/zones", new
        {
            name = "Papyon",
            corners = new[] { new[] { 41.25, 36.25 }, new[] { 41.36, 36.40 }, new[] { 41.36, 36.25 }, new[] { 41.25, 36.40 } },
            deliveryFee = 30m,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_cheapest_active_zone_covering_a_point_applies()
    {
        var firm = await factory.FirmAsync();
        await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/zones", new
        {
            name = "Atakum sahil",
            corners = new[] { new[] { 41.32, 36.26 }, new[] { 41.32, 36.30 }, new[] { 41.35, 36.30 }, new[] { 41.35, 36.26 } },
            deliveryFee = 30m,
        });

        async Task<ZoneCheckDto> Check(double lat, double lng) =>
            await (await firm.Owner.Http.GetAsync($"{firm.Path}/zones/check?lat={lat}&lng={lng}")).ReadAsync<ZoneCheckDto>();

        Assert.Equal(("Atakum sahil", 30m), ((await Check(41.33, 36.28)).ZoneName, (await Check(41.33, 36.28)).DeliveryFee!.Value));
        Assert.Equal("Merkez", (await Check(41.28, 36.35)).ZoneName);
        Assert.False((await Check(41.56, 35.90)).Delivers);
    }

    [Fact]
    public async Task Merchant_members_only_see_their_own_merchant()
    {
        var firm = await factory.FirmAsync();
        var second = await TestApi.MerchantAsync(firm.Owner, firm.Path, "Kebapçı Halil", 41.29, 36.34);
        using var cashier = await factory.RegisterAsync("Kasiyer");
        await firm.AddMemberAsync(cashier, "Merchant", firm.MerchantId);

        var mine = await (await cashier.Http.GetAsync($"{firm.Path}/merchants")).ReadAsync<List<MerchantDto>>();
        var peek = await cashier.Http.GetAsync($"{firm.Path}/merchants/{second.Id}");
        var all = await (await firm.Owner.Http.GetAsync($"{firm.Path}/merchants?q=kebap")).ReadAsync<List<MerchantDto>>();

        Assert.Equal(["Lezzet Pide"], mine.Select(m => m.Name));
        Assert.Equal(HttpStatusCode.NotFound, peek.StatusCode);
        Assert.Equal(["Kebapçı Halil"], all.Select(m => m.Name));
    }

    [Fact]
    public async Task Couriers_set_their_own_status_and_dispatchers_their_vehicle()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.CourierAsync(factory, online: false);

        var online = await (await ali.PostAsync($"{firm.Path}/couriers/me/status", new { online = true })).ReadAsync<CourierDto>();
        var vehicle = await (await firm.Owner.Http.PutAsJsonAsync($"{firm.Path}/couriers/{ali.Id}", new { vehicle = "Bicycle", plate = "" })).ReadAsync<CourierDto>();
        var byCourier = await ali.Http.PutAsJsonAsync($"{firm.Path}/couriers/{ali.Id}", new { vehicle = "Car" });
        var list = await ali.Http.GetAsync($"{firm.Path}/couriers");

        Assert.Equal("Available", online.Status);
        Assert.Equal("Bicycle", vehicle.Vehicle);
        Assert.Equal(HttpStatusCode.Forbidden, byCourier.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }
}
