using Kuryo.Core.Geo;
using NetTopologySuite.Geometries;

namespace Kuryo.Core.Zones;

/// <summary>An area the company delivers to, with what a delivery there costs. Areas may overlap; the cheapest wins.</summary>
public sealed class Zone
{
    public const int NameMaxLength = 60;

    private Zone()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public Polygon Area { get; private set; } = null!;

    /// <summary>In kuruş.</summary>
    public long DeliveryFeeMinor { get; private set; }

    public bool Active { get; private set; }

    public static Zone Create(Guid companyId, string name, IReadOnlyList<double[]> corners, decimal deliveryFee, DateTimeOffset now)
    {
        var zone = new Zone { Id = Guid.CreateVersion7(now), CompanyId = companyId };
        zone.Update(name, corners, deliveryFee, true);
        return zone;
    }

    public void Update(string name, IReadOnlyList<double[]> corners, decimal deliveryFee, bool active)
    {
        if (deliveryFee is < 0 or > 10_000 || decimal.Round(deliveryFee, 2) != deliveryFee)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A delivery fee is 0 to 10,000 TRY, to the kuruş.");
        }

        Name = Text.Clean(name, 2, NameMaxLength, "A zone's name");
        Area = GeoPoint.Area(corners);
        DeliveryFeeMinor = decimal.ToInt64(deliveryFee * 100);
        Active = active;
    }

    public bool Contains(Point point) => Area.Covers(point);

    /// <summary>The corners as [latitude, longitude], without repeating the first at the end.</summary>
    public IReadOnlyList<double[]> Corners => Area.Shell.Coordinates.SkipLast(1).Select(c => new[] { c.Y, c.X }).ToList();
}
