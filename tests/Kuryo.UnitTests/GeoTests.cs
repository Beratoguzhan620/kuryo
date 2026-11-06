using Kuryo.Core;
using Kuryo.Core.Couriers;
using Kuryo.Core.Geo;

namespace Kuryo.UnitTests;

public sealed class GeoTests
{
    [Fact]
    public void Points_are_longitude_first_like_PostGIS_and_rounded_to_a_tenth_of_a_metre()
    {
        var point = GeoPoint.At(41.28671234, 36.33004567);

        Assert.Equal((36.330046, 41.286712, 4326), (point.X, point.Y, point.SRID));
    }

    [Theory]
    [InlineData(91, 36)]
    [InlineData(41, 181)]
    [InlineData(double.NaN, 36)]
    public void Points_must_be_on_the_earth(double lat, double lng) =>
        Assert.Throws<DomainException>(() => GeoPoint.At(lat, lng));

    [Fact]
    public void Distances_are_great_circle_kilometres()
    {
        var centre = GeoPoint.At(41.2867, 36.33);

        Assert.InRange(GeoPoint.DistanceKm(centre, GeoPoint.At(41.3335, 36.2714)), 7.0, 7.3);
        Assert.InRange(GeoPoint.DistanceKm(centre, GeoPoint.At(39.9334, 32.8597)), 325, 335);
    }

    [Theory]
    [InlineData(4, 24, 14)]
    [InlineData(4, 14, 24)]
    [InlineData(0, 24, 0)]
    [InlineData(0.1, 24, 1)]
    public void Riding_time_adds_the_detour_and_rounds_up_to_the_minute(double km, double speed, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), GeoPoint.RideTime(km, speed));

    [Fact]
    public void Areas_are_closed_for_you_and_always_wound_the_same_way()
    {
        var clockwise = GeoPoint.Area([[41.25, 36.25], [41.36, 36.25], [41.36, 36.40], [41.25, 36.40]]);
        var counter = GeoPoint.Area(Builders.Centre);

        Assert.Equal(5, clockwise.Shell.NumPoints);
        Assert.True(clockwise.Shell.IsCCW);
        Assert.True(clockwise.EqualsTopologically(counter));
    }

    [Fact]
    public void Areas_with_crossing_edges_or_too_few_corners_are_refused()
    {
        Assert.Throws<DomainException>(() => GeoPoint.Area([[41.25, 36.25], [41.36, 36.40], [41.36, 36.25], [41.25, 36.40]]));
        Assert.Throws<DomainException>(() => GeoPoint.Area([[41.25, 36.25], [41.36, 36.40]]));
        Assert.Throws<DomainException>(() => GeoPoint.Area([[41.25, 36.25], [41.30, 36.30], [41.35, 36.35]]));
    }

    [Fact]
    public void Each_vehicle_has_a_town_speed()
    {
        Assert.Equal((14d, 24d, 20d), (Courier.SpeedKmh(Vehicle.Bicycle), Courier.SpeedKmh(Vehicle.Scooter), Courier.SpeedKmh(Vehicle.Car)));
        Assert.Throws<DomainException>(() => Courier.SpeedKmh((Vehicle)9));
    }
}
