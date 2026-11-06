using System.Net;
using System.Net.Http.Json;
using Kuryo.IntegrationTests.Support;

namespace Kuryo.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class TeamTests(ApiFactory factory)
{
    [Fact]
    public async Task Creating_a_company_makes_you_its_owner_with_default_offer_settings()
    {
        using var person = await factory.RegisterAsync();

        var created = await (await person.Http.PostAsJsonAsync("/api/companies", new { name = "  Hızlı   Kurye " })).ReadAsync<CompanyDto>();

        Assert.Equal(("Hızlı Kurye", "Owner", 30, 6.0, 5), (created.Name, created.Role, created.OfferSeconds, created.SearchRadiusKm, created.MaxOffers));
    }

    [Fact]
    public async Task Owners_change_the_offer_settings_and_nobody_else_can()
    {
        var firm = await factory.FirmAsync();
        using var dispatcher = await factory.RegisterAsync("Planlamacı");
        await firm.AddMemberAsync(dispatcher, "Dispatcher");
        var body = new { name = "Hızlı Kurye", offerSeconds = 45, searchRadiusKm = 4.5, maxOffers = 3 };

        var byDispatcher = await dispatcher.Http.PutAsJsonAsync(firm.Path, body);
        var byOwner = await (await firm.Owner.Http.PutAsJsonAsync(firm.Path, body)).ReadAsync<CompanyDto>();
        var silly = await firm.Owner.Http.PutAsJsonAsync(firm.Path, body with { offerSeconds = 2 });

        Assert.Equal(HttpStatusCode.Forbidden, byDispatcher.StatusCode);
        Assert.Equal((45, 4.5, 3), (byOwner.OfferSeconds, byOwner.SearchRadiusKm, byOwner.MaxOffers));
        Assert.Equal(HttpStatusCode.BadRequest, silly.StatusCode);
    }

    [Fact]
    public async Task Merchant_members_belong_to_a_merchant_of_the_company()
    {
        var firm = await factory.FirmAsync();
        var other = await factory.FirmAsync("Başka Kurye");
        using var cashier = await factory.RegisterAsync("Kasiyer");

        var none = await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/members", new { email = cashier.Email, role = "Merchant" });
        var foreign = await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/members", new { email = cashier.Email, role = "Merchant", merchantId = other.MerchantId });
        var member = await firm.AddMemberAsync(cashier, "Merchant", firm.MerchantId);

        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(("Merchant", "Lezzet Pide"), (member.Role, member.MerchantName));
    }

    [Fact]
    public async Task Couriers_get_a_profile_when_they_join_and_lose_it_when_their_role_changes()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.CourierAsync(factory, online: false);

        var before = await (await firm.Owner.Http.GetAsync($"{firm.Path}/couriers")).ReadAsync<List<CourierDto>>();
        (await firm.Owner.Http.PatchAsJsonAsync($"{firm.Path}/members/{ali.Id}", new { role = "Dispatcher" })).EnsureSuccessStatusCode();
        var after = await (await firm.Owner.Http.GetAsync($"{firm.Path}/couriers")).ReadAsync<List<CourierDto>>();

        Assert.Equal(("Kurye Ali", "Scooter", "Offline"), (before.Single().DisplayName, before.Single().Vehicle, before.Single().Status));
        Assert.Empty(after);
    }

    [Fact]
    public async Task A_courier_carrying_an_order_cannot_be_removed()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.CourierAsync(factory);
        var order = await firm.OrderAsync();
        (await firm.AssignAsync(order.Id, ali)).EnsureSuccessStatusCode();

        var remove = await firm.Owner.Http.DeleteAsync($"{firm.Path}/members/{ali.Id}");

        Assert.Equal(HttpStatusCode.Conflict, remove.StatusCode);
    }

    [Fact]
    public async Task Outsiders_cannot_tell_a_company_exists_and_the_last_owner_stays()
    {
        var firm = await factory.FirmAsync();
        using var outsider = await factory.RegisterAsync("Yabancı");

        var theirs = await outsider.Http.GetAsync($"{firm.Path}/orders");
        var missing = await outsider.Http.GetAsync($"/api/companies/{Guid.NewGuid()}/orders");
        var leave = await firm.Owner.Http.DeleteAsync($"{firm.Path}/members/{firm.Owner.Id}");

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (theirs.StatusCode, missing.StatusCode));
        Assert.Equal(HttpStatusCode.Conflict, leave.StatusCode);
    }
}
