using Kuryo.Core.Geo;
using NetTopologySuite.Geometries;

namespace Kuryo.Core.Merchants;

/// <summary>A restaurant or shop the company delivers for. Couriers pick up at its address.</summary>
public sealed class Merchant
{
    public const int NameMaxLength = 100;
    public const int AddressMaxLength = 200;

    private Merchant()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Phone { get; private set; } = string.Empty;

    public string Address { get; private set; } = string.Empty;

    public Point Location { get; private set; } = null!;

    /// <summary>How long an order usually takes to be ready; offers go out so the courier arrives about then.</summary>
    public int PrepMinutes { get; private set; }

    /// <summary>Paused merchants keep their history but cannot send orders.</summary>
    public bool Active { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Merchant Create(Guid companyId, string name, string? phone, string address, double latitude, double longitude, int prepMinutes, DateTimeOffset now)
    {
        var merchant = new Merchant { Id = Guid.CreateVersion7(now), CompanyId = companyId, CreatedAt = now };
        merchant.Update(name, phone, address, latitude, longitude, prepMinutes, true);
        return merchant;
    }

    public void Update(string name, string? phone, string address, double latitude, double longitude, int prepMinutes, bool active)
    {
        if (prepMinutes is < 0 or > 120)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Preparation takes 0 to 120 minutes.");
        }

        Name = Text.Clean(name, 2, NameMaxLength, "A merchant's name");
        Phone = string.IsNullOrWhiteSpace(phone) ? string.Empty : Text.Clean(phone, 3, 30, "A phone number");
        Address = Text.Clean(address, 3, AddressMaxLength, "An address");
        Location = GeoPoint.At(latitude, longitude);
        PrepMinutes = prepMinutes;
        Active = active;
    }
}
