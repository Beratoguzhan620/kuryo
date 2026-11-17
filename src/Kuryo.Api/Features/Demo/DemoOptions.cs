namespace Kuryo.Api.Features.Demo;

/// <summary>
/// The public demo: a courier company in Samsun whose couriers drive themselves, so the live map always has something
/// on it, and three sign-ins it publishes (dispatcher, merchant, courier). Off everywhere else.
/// </summary>
public sealed class DemoOptions
{
    public const string Section = "Demo";

    public bool Enabled { get; set; }

    /// <summary>Where this API listens, for the demo's own calls.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:8080";

    /// <summary>How often the simulated couriers move, take offers and finish deliveries.</summary>
    public TimeSpan Tick { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How often a merchant sends a new order while fewer than <see cref="OpenOrders"/> are open.</summary>
    public TimeSpan OrderEvery { get; set; } = TimeSpan.FromSeconds(75);

    public int OpenOrders { get; set; } = 4;

    public string DispatcherEmail { get; set; } = "dispatcher@kuryo.demo";

    public string MerchantEmail { get; set; } = "merchant@kuryo.demo";

    public string CourierEmail { get; set; } = "courier@kuryo.demo";

    /// <summary>Shared by every demo account; published on the sign-in page, so it guards nothing.</summary>
    public string Password { get; set; } = "kuryo-demo-password";
}
