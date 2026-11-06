using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kuryo.IntegrationTests.Support;

/// <summary>A signed-in person with an HTTP client that carries their token.</summary>
public sealed class Person(ApiFactory factory, SessionDto session) : IDisposable
{
    public HttpClient Http { get; } = Client(factory, session.AccessToken);

    public Guid Id => session.User.Id;

    public string Email => session.User.Email;

    public string Name => session.User.DisplayName;

    public string Token => session.AccessToken;

    public void Dispose() => Http.Dispose();

    private static HttpClient Client(ApiFactory factory, string token)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }
}

/// <summary>A courier company with its owner, a zone over central Samsun and one merchant in it.</summary>
public sealed record Firm(Person Owner, Guid Id, Guid ZoneId, Guid MerchantId)
{
    public string Path => $"/api/companies/{Id}";
}

public static class TestApi
{
    public const string Password = "correct-horse-battery";

    // Central Samsun, from the edge of Atakum to Canik.
    public static readonly double[][] Centre = [[41.25, 36.25], [41.25, 36.40], [41.36, 36.40], [41.36, 36.25]];

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static async Task<Person> RegisterAsync(this ApiFactory factory, string name = "Ayşe Yılmaz")
    {
        using var anonymous = factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var response = await anonymous.PostAsJsonAsync("/api/auth/register", new { email, displayName = name, password = Password });
        response.EnsureSuccessStatusCode();
        return new Person(factory, await response.ReadAsync<SessionDto>());
    }

    public static async Task<Firm> FirmAsync(this ApiFactory factory, string name = "Hızlı Kurye")
    {
        var owner = await factory.RegisterAsync("Deniz Sahibi");
        var company = await (await owner.Http.PostAsJsonAsync("/api/companies", new { name })).ReadAsync<CompanyDto>();
        var path = $"/api/companies/{company.Id}";
        var zone = await (await owner.Http.PostAsJsonAsync($"{path}/zones", new { name = "Merkez", corners = Centre, deliveryFee = 45m })).ReadAsync<ZoneDto>();
        var merchant = await MerchantAsync(owner, path);
        return new Firm(owner, company.Id, zone.Id, merchant.Id);
    }

    public static async Task<MerchantDto> MerchantAsync(Person by, string path, string name = "Lezzet Pide", double latitude = 41.2867, double longitude = 36.33)
    {
        var response = await by.Http.PostAsJsonAsync($"{path}/merchants", new { name, phone = "0362 000 00 00", address = "İstiklal Cd. 10, İlkadım", latitude, longitude, prepMinutes = 15 });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<MerchantDto>();
    }

    public static async Task<MemberDto> AddMemberAsync(this Firm firm, Person member, string role, Guid? merchantId = null)
    {
        var response = await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/members", new { email = member.Email, role, merchantId });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<MemberDto>();
    }

    /// <summary>A courier on the team who has gone online.</summary>
    public static async Task<Person> CourierAsync(this Firm firm, ApiFactory factory, string name = "Kurye Ali", bool online = true)
    {
        var person = await factory.RegisterAsync(name);
        await firm.AddMemberAsync(person, "Courier");
        if (online)
        {
            (await person.Http.PostAsJsonAsync($"{firm.Path}/couriers/me/status", new { online = true })).EnsureSuccessStatusCode();
        }

        return person;
    }

    public static object OrderBody(Guid merchantId, double latitude = 41.3300, double longitude = 36.2900, string customer = "Ayşe Kaya") => new
    {
        merchantId,
        customerName = customer,
        customerPhone = "0532 000 00 00",
        dropoffAddress = "Atatürk Blv. 50, Atakum",
        latitude,
        longitude,
        dropoffNotes = "Kat 3, zil 7",
        items = "2 kıymalı pide",
    };

    public static async Task<OrderDto> OrderAsync(this Firm firm, Person? by = null, object? body = null)
    {
        var response = await (by ?? firm.Owner).Http.PostAsJsonAsync($"{firm.Path}/orders", body ?? OrderBody(firm.MerchantId));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<OrderDto>();
    }

    public static async Task<HttpResponseMessage> AssignAsync(this Firm firm, Guid orderId, Person courier) =>
        await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/orders/{orderId}/assign", new { courierId = courier.Id });

    public static async Task<HttpResponseMessage> PostAsync(this Person person, string path, object? body = null) =>
        await person.Http.PostAsJsonAsync(path, body ?? new { });

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
