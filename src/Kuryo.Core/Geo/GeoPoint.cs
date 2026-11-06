using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace Kuryo.Core.Geo;

/// <summary>Points and areas on the earth (WGS 84), and the distances couriers ride between them.</summary>
public static class GeoPoint
{
    public const int Srid = 4326;

    /// <summary>Roads are longer than a straight line; this is how much longer, on average, in a Turkish city.</summary>
    public const double DetourFactor = 1.35;

    public static readonly GeometryFactory Factory = NtsGeometryServices.Instance.CreateGeometryFactory(Srid);

    public static Point At(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A location needs a latitude from -90 to 90 and a longitude from -180 to 180.");
        }

        // X is longitude and Y latitude, as PostGIS expects.
        return Factory.CreatePoint(new Coordinate(Math.Round(longitude, 6), Math.Round(latitude, 6)));
    }

    public static double Latitude(this Point point) => point.Y;

    public static double Longitude(this Point point) => point.X;

    /// <summary>Great-circle distance in kilometres; PostGIS gives the same on a sphere.</summary>
    public static double DistanceKm(Point a, Point b)
    {
        const double EarthRadiusKm = 6371.0088;
        static double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(b.Y - a.Y);
        var dLon = Rad(b.X - a.X);
        var h = Math.Pow(Math.Sin(dLat / 2), 2) + (Math.Cos(Rad(a.Y)) * Math.Cos(Rad(b.Y)) * Math.Pow(Math.Sin(dLon / 2), 2));
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    /// <summary>Riding time for a straight-line distance at a speed, with the detour roads add.</summary>
    public static TimeSpan RideTime(double straightKm, double speedKmh) =>
        TimeSpan.FromMinutes(Math.Ceiling(straightKm * DetourFactor / speedKmh * 60));

    /// <summary>A delivery area from its corners, as [latitude, longitude] pairs; the ring is closed for you.</summary>
    public static Polygon Area(IReadOnlyList<double[]> corners)
    {
        if (corners.Count is < 3 or > 200 || corners.Any(c => c.Length != 2))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "An area has 3 to 200 corners, each [latitude, longitude].");
        }

        var points = corners.Select(c => At(c[0], c[1]).Coordinate).ToList();
        if (!points[0].Equals2D(points[^1]))
        {
            points.Add(points[0].Copy());
        }

        var polygon = Factory.CreatePolygon(points.ToArray());
        if (!polygon.IsValid || polygon.Area == 0)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "The area's edges cross or it has no size. Draw the corners in order around it.");
        }

        // Always the same winding, so the same area is stored the same way.
        return polygon.Shell.IsCCW ? polygon : (Polygon)polygon.Reverse();
    }
}
