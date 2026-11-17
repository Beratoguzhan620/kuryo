using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Kuryo.Api.Features.Demo;

/// <summary>
/// Runs the public demo's courier company, entirely through the API's own endpoints, so everything on the map passes
/// the same rules as anything a visitor does. On start it makes the company once (zones, merchants, couriers and the
/// published sign-ins). Then, while the service is awake, merchants send orders now and then, and five simulated
/// couriers keep their positions fresh, take most offers and pass some, ride to the merchant, pick up, ride to the
/// customer and finish with the code. Visitors watch it on the live map, or sign in as the merchant to send their own.
/// </summary>
internal sealed partial class DemoWorker(
    IOptions<DemoOptions> options, IHttpClientFactory clients, IHostApplicationLifetime lifetime, TimeProvider clock, ILogger<DemoWorker> logger)
    : BackgroundService
{
    public const string HttpClientName = "Kuryo.Demo";

    private const string CompanyName = "Hızlı Kurye Samsun";

    /// <summary>How far a simulated courier moves each tick, in kilometres: faster than life, so a delivery fits a visit.</summary>
    private const double StepKm = 0.35;

    private static readonly (string Key, string Name, string Address, double Lat, double Lng, int Prep)[] Shops =
    [
        ("pide", "Lezzet Pide", "İstiklal Cd. 10, İlkadım", 41.2867, 36.3300, 10),
        ("kebap", "Kebapçı Halil", "Bahçelievler Sk. 4, İlkadım", 41.2905, 36.3205, 12),
        ("firin", "Köşe Fırın", "Atatürk Blv. 120, Atakum", 41.3290, 36.2700, 5),
        ("eczane", "Liman Eczanesi", "Gazi Cd. 33, Canik", 41.2720, 36.3550, 0),
    ];

    private static readonly (string Key, string Name, string Vehicle, string Plate, double Lat, double Lng)[] Crew =
    [
        ("ali", "Ali Kaya", "Scooter", "55 AK 101", 41.2885, 36.3290),
        ("veli", "Veli Demir", "Scooter", "55 VD 202", 41.2990, 36.3150),
        ("zeynep", "Zeynep Arslan", "Bicycle", "", 41.3270, 36.2760),
        ("mert", "Mert Öztürk", "Car", "55 MO 303", 41.2750, 36.3480),
        ("elif", "Elif Şahin", "Scooter", "55 ES 404", 41.3100, 36.3000),
    ];

    private static readonly (string Name, string Address, double Lat, double Lng, string Items)[] Customers =
    [
        ("Ayşe Kaya", "Atatürk Blv. 50, Atakum", 41.3210, 36.2950, "2 kıymalı pide, 1 ayran"),
        ("Murat Koç", "Kale Mh. 3, İlkadım", 41.2960, 36.3400, "Adana dürüm"),
        ("Hakan Çelik", "Mimar Sinan Mh. 12, Atakum", 41.3335, 36.2640, "Simit, poğaça"),
        ("Fatma Aydın", "Yavuz Selim Cd. 8, Canik", 41.2650, 36.3700, "Reçeteli ilaç"),
        ("Selin Aksoy", "Kurupelit Mh. 7, Atakum", 41.3080, 36.3060, "1 karışık pide"),
        ("Derya Uçar", "Körfez Mh. 22, Atakum", 41.3300, 36.2560, "2 açma, 1 boyoz"),
        ("Emre Yurt", "Cumhuriyet Mh. 15, İlkadım", 41.2840, 36.3380, "Lahmacun, ayran"),
        ("Gül Tan", "Karşıyaka Mh. 9, Canik", 41.2700, 36.3620, "Ateş düşürücü"),
    ];

    private readonly Random _random = new(16);
    private readonly Dictionary<string, Bot> _bots = [];
    private readonly Dictionary<Guid, string> _codes = [];
    private Guid _company;
    private string _dispatcher = "";
    private readonly Dictionary<string, Guid> _shops = [];
    private DateTimeOffset _nextOrder;
    private DateTimeOffset _tokensAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await StartedAsync(stoppingToken))
        {
            return;
        }

        var demo = options.Value;
        using var http = clients.CreateClient(HttpClientName);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_company == Guid.Empty || clock.GetUtcNow() - _tokensAt > TimeSpan.FromMinutes(45))
                {
                    await SetUpAsync(http, stoppingToken);
                }

                await TickAsync(http, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // The demo keeps going; the next tick tries again.
                LogTickFailed(logger, ex);
            }

            await Task.Delay(demo.Tick, clock, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> StartedAsync(CancellationToken ct)
    {
        var started = new TaskCompletionSource();
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await Task.WhenAny(started.Task, Task.Delay(Timeout.Infinite, ct)).ConfigureAwait(false);
        return started.Task.IsCompleted && !ct.IsCancellationRequested;
    }

    /// <summary>Signs everyone in, making the company and its people the first time.</summary>
    public async Task SetUpAsync(HttpClient http, CancellationToken ct)
    {
        var demo = options.Value;
        var dispatcher = await SignInAsync(http, demo.DispatcherEmail, "Deniz Yılmaz", ct);
        _dispatcher = dispatcher.Token;

        foreach (var member in Crew)
        {
            var session = await SignInAsync(http, BotEmail(member.Key), member.Name, ct);
            if (!_bots.TryGetValue(member.Key, out var bot))
            {
                bot = new Bot(session.UserId, member.Lat, member.Lng);
                _bots[member.Key] = bot;
            }

            bot.Token = session.Token;
        }

        var me = await SendAsync<Me>(http, HttpMethod.Get, "/api/me", _dispatcher, null, ct);
        var company = me.Companies.FirstOrDefault(c => c.Name == CompanyName);
        if (company is null)
        {
            company = await SendAsync<Named>(http, HttpMethod.Post, "/api/companies", _dispatcher, new { name = CompanyName }, ct);
            await CreateCompanyAsync(http, company.Id, ct);
        }

        _company = company.Id;
        var c = $"/api/companies/{_company}";
        foreach (var shop in await SendAsync<List<Named>>(http, HttpMethod.Get, $"{c}/merchants", _dispatcher, null, ct))
        {
            var key = Shops.FirstOrDefault(s => s.Name == shop.Name).Key;
            if (key is not null)
            {
                _shops[key] = shop.Id;
            }
        }

        foreach (var bot in _bots.Values)
        {
            await SendAsync<JsonElement?>(http, HttpMethod.Post, $"{c}/couriers/me/status", bot.Token, new { online = true }, ct);
        }

        _tokensAt = clock.GetUtcNow();
        _nextOrder = clock.GetUtcNow();
        LogReady(logger, CompanyName);
    }

    private async Task CreateCompanyAsync(HttpClient http, Guid company, CancellationToken ct)
    {
        var demo = options.Value;
        var c = $"/api/companies/{company}";
        await SendAsync<JsonElement?>(http, HttpMethod.Put, c, _dispatcher, new { name = CompanyName, offerSeconds = 30, searchRadiusKm = 6, maxOffers = 5 }, ct);
        await SendAsync<JsonElement?>(http, HttpMethod.Post, $"{c}/zones", _dispatcher,
            new { name = "Atakum", corners = new double[][] { [41.318, 36.215], [41.350, 36.215], [41.350, 36.300], [41.318, 36.300] }, deliveryFee = 55 }, ct);
        await SendAsync<JsonElement?>(http, HttpMethod.Post, $"{c}/zones", _dispatcher,
            new { name = "İlkadım", corners = new double[][] { [41.268, 36.290], [41.318, 36.290], [41.318, 36.350], [41.268, 36.350] }, deliveryFee = 40 }, ct);
        await SendAsync<JsonElement?>(http, HttpMethod.Post, $"{c}/zones", _dispatcher,
            new { name = "Canik", corners = new double[][] { [41.248, 36.340], [41.290, 36.340], [41.290, 36.400], [41.248, 36.400] }, deliveryFee = 50 }, ct);

        foreach (var shop in Shops)
        {
            await SendAsync<Named>(http, HttpMethod.Post, $"{c}/merchants", _dispatcher,
                new { name = shop.Name, phone = "0362 431 00 00", address = shop.Address, latitude = shop.Lat, longitude = shop.Lng, prepMinutes = shop.Prep }, ct);
        }

        var merchants = await SendAsync<List<Named>>(http, HttpMethod.Get, $"{c}/merchants", _dispatcher, null, ct);
        var pide = merchants.First(m => m.Name == "Lezzet Pide").Id;

        foreach (var member in Crew)
        {
            await SendAsync<JsonElement?>(http, HttpMethod.Post, $"{c}/members", _dispatcher, new { email = BotEmail(member.Key), role = "Courier" }, ct);
            await SendAsync<JsonElement?>(http, HttpMethod.Put, $"{c}/couriers/{_bots[member.Key].UserId}", _dispatcher, new { vehicle = member.Vehicle, plate = member.Plate }, ct);
        }

        // The sign-ins visitors use: the merchant sends orders from Lezzet Pide, the courier is theirs to drive.
        await SignInAsync(http, demo.MerchantEmail, "Selin Kasa", ct);
        await SendAsync<JsonElement?>(http, HttpMethod.Post, $"{c}/members", _dispatcher, new { email = demo.MerchantEmail, role = "Merchant", merchantId = pide }, ct);
        var courier = await SignInAsync(http, demo.CourierEmail, "Can Yıldız", ct);
        await SendAsync<JsonElement?>(http, HttpMethod.Post, $"{c}/members", _dispatcher, new { email = demo.CourierEmail, role = "Courier" }, ct);
        await SendAsync<JsonElement?>(http, HttpMethod.Put, $"{c}/couriers/{courier.UserId}", _dispatcher, new { vehicle = "Bicycle", plate = "" }, ct);
    }

    /// <summary>One step of the evening: maybe a new order, then every courier does what its situation asks.</summary>
    public async Task TickAsync(HttpClient http, CancellationToken ct)
    {
        var c = $"/api/companies/{_company}";
        var now = clock.GetUtcNow();
        if (now >= _nextOrder)
        {
            var company = await SendAsync<CompanyInfo>(http, HttpMethod.Get, c, _dispatcher, null, ct);
            if (company.OpenOrders < options.Value.OpenOrders)
            {
                await SendOrderAsync(http, ct);
            }

            _nextOrder = now + options.Value.OrderEvery;
        }

        foreach (var bot in _bots.Values)
        {
            await StepAsync(http, bot, ct);
        }

        // An order nobody took goes out again, as a dispatcher would send it.
        var stuck = await SendAsync<Page>(http, HttpMethod.Get, $"{c}/orders?status=Unassigned&pageSize=20", _dispatcher, null, ct);
        foreach (var order in stuck.Items)
        {
            await SendAsync<JsonElement?>(http, HttpMethod.Post, $"{c}/orders/{order.Id}/search-again", _dispatcher, new { }, ct);
        }
    }

    private async Task SendOrderAsync(HttpClient http, CancellationToken ct)
    {
        if (_shops.Count == 0)
        {
            return;
        }

        var shop = Shops[_random.Next(Shops.Length)];
        var customer = Customers[_random.Next(Customers.Length)];
        var order = await SendAsync<OrderInfo>(http, HttpMethod.Post, $"/api/companies/{_company}/orders", _dispatcher, new
        {
            merchantId = _shops[shop.Key],
            customerName = customer.Name,
            customerPhone = "0532 000 00 00",
            dropoffAddress = customer.Address,
            latitude = customer.Lat,
            longitude = customer.Lng,
            dropoffNotes = "",
            items = customer.Items,
        }, ct);
        if (order.DeliveryCode is not null)
        {
            _codes[order.Id] = order.DeliveryCode;
        }
    }

    private async Task StepAsync(HttpClient http, Bot bot, CancellationToken ct)
    {
        var c = $"/api/companies/{_company}";
        var me = await SendAsync<CourierInfo>(http, HttpMethod.Get, $"{c}/couriers/me", bot.Token, null, ct);
        if (me.OrderId is { } orderId)
        {
            var order = await SendAsync<OrderInfo>(http, HttpMethod.Get, $"{c}/orders/{orderId}", bot.Token, null, ct);
            var target = order.Status == "Assigned" ? order.Pickup : order.Dropoff;
            var arrived = bot.MoveTowards(target.Latitude, target.Longitude, StepKm);
            await PingAsync(http, bot, ct);
            if (!arrived)
            {
                return;
            }

            if (order.Status == "Assigned" && order.ReadyAt <= clock.GetUtcNow())
            {
                await SendAsync<JsonElement?>(http, HttpMethod.Post, $"{c}/orders/{orderId}/pickup", bot.Token, new { }, ct);
            }
            else if (order.Status == "PickedUp")
            {
                var code = await CodeAsync(http, orderId, ct);
                await SendAsync<JsonElement?>(http, HttpMethod.Post, $"{c}/orders/{orderId}/deliver", bot.Token, new { code }, ct);
                _codes.Remove(orderId);
            }

            return;
        }

        // Free: drift around town, and take what is offered, passing about one offer in five.
        bot.Wander(_random, StepKm / 3);
        await PingAsync(http, bot, ct);
        var offer = await SendAsync<OfferInfo?>(http, HttpMethod.Get, $"{c}/couriers/me/offer", bot.Token, null, ct);
        if (offer is not null)
        {
            var answer = _random.Next(5) == 0 ? "decline" : "accept";
            await SendAsync<JsonElement?>(http, HttpMethod.Post, $"{c}/offers/{offer.Id}/{answer}", bot.Token, new { }, ct);
        }
    }

    /// <summary>The customer's code: remembered from the order's creation, or read back as the dispatcher can.</summary>
    private async Task<string> CodeAsync(HttpClient http, Guid orderId, CancellationToken ct)
    {
        if (_codes.TryGetValue(orderId, out var code))
        {
            return code;
        }

        var order = await SendAsync<OrderInfo>(http, HttpMethod.Get, $"/api/companies/{_company}/orders/{orderId}", _dispatcher, null, ct);
        return order.DeliveryCode ?? throw new InvalidOperationException($"No code for order {orderId}.");
    }

    private Task PingAsync(HttpClient http, Bot bot, CancellationToken ct) =>
        SendAsync<JsonElement?>(http, HttpMethod.Post, $"/api/companies/{_company}/couriers/me/locations", bot.Token,
            new { points = new[] { new { latitude = bot.Latitude, longitude = bot.Longitude, at = clock.GetUtcNow() } } }, ct);

    private static string BotEmail(string key) => $"{key}@couriers.kuryo.demo";

    private async Task<Session> SignInAsync(HttpClient http, string email, string name, CancellationToken ct)
    {
        var password = options.Value.Password;
        using var login = await http.PostAsJsonAsync("/api/auth/login", new { email, password }, ct);
        using var register = login.IsSuccessStatusCode
            ? null
            : await http.PostAsJsonAsync("/api/auth/register", new { email, displayName = name, password }, ct);
        var response = register ?? login;
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"The demo account {email} could not sign in: {(int)response.StatusCode}.");
        }

        var session = (await response.Content.ReadFromJsonAsync<SessionInfo>(ct))!;
        return new Session(session.AccessToken, session.User.Id);
    }

    private static async Task<T> SendAsync<T>(HttpClient http, HttpMethod method, string path, string token, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{method} {path} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
        }

        return response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0
            ? default!
            : (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }

    private sealed class Bot(Guid userId, double latitude, double longitude)
    {
        public Guid UserId { get; } = userId;

        public string Token { get; set; } = "";

        public double Latitude { get; private set; } = latitude;

        public double Longitude { get; private set; } = longitude;

        /// <summary>Moves at most <paramref name="km"/> towards a point; true once there.</summary>
        public bool MoveTowards(double latitude, double longitude, double km)
        {
            var dLat = (latitude - Latitude) * 111.0;
            var dLng = (longitude - Longitude) * 111.0 * Math.Cos(Latitude * Math.PI / 180);
            var distance = Math.Sqrt((dLat * dLat) + (dLng * dLng));
            if (distance <= km)
            {
                Latitude = latitude;
                Longitude = longitude;
                return true;
            }

            Latitude += dLat / distance * km / 111.0;
            Longitude += dLng / distance * km / (111.0 * Math.Cos(Latitude * Math.PI / 180));
            return false;
        }

        /// <summary>A small random move, kept inside the town's delivery area.</summary>
        public void Wander(Random random, double km)
        {
            var angle = random.NextDouble() * 2 * Math.PI;
            Latitude = Math.Clamp(Latitude + (Math.Sin(angle) * km / 111.0), 41.255, 41.345);
            Longitude = Math.Clamp(Longitude + (Math.Cos(angle) * km / 83.5), 36.230, 36.390);
        }
    }

    private sealed record Session(string Token, Guid UserId);

    private sealed record SessionInfo(string AccessToken, UserInfo User);

    private sealed record UserInfo(Guid Id);

    private sealed record Named(Guid Id, string Name);

    private sealed record Me(List<Named> Companies);

    private sealed record CompanyInfo(int OpenOrders);

    private sealed record CourierInfo(Guid? OrderId);

    private sealed record Place(double Latitude, double Longitude);

    private sealed record OrderInfo(Guid Id, string Status, Place Pickup, Place Dropoff, DateTimeOffset ReadyAt, string? DeliveryCode);

    private sealed record OfferInfo(Guid Id);

    private sealed record IdOnly(Guid Id);

    private sealed record Page(List<IdOnly> Items);

    [LoggerMessage(Level = LogLevel.Information, Message = "The demo company {Company} is running")]
    private static partial void LogReady(ILogger log, string company);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A demo tick failed")]
    private static partial void LogTickFailed(ILogger log, Exception exception);
}
