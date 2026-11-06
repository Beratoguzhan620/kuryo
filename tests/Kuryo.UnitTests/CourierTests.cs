using Kuryo.Core;
using Kuryo.Core.Couriers;
using Kuryo.Core.Geo;

namespace Kuryo.UnitTests;

public sealed class CourierTests
{
    private static Courier New() => Courier.Create(Guid.NewGuid(), Guid.NewGuid(), Vehicle.Scooter, " 55 abc 123 ", Builders.Now);

    [Fact]
    public void A_new_courier_starts_offline_with_a_tidy_plate()
    {
        var courier = New();

        Assert.Equal((CourierStatus.Offline, "55 ABC 123"), (courier.Status, courier.Plate));
    }

    [Fact]
    public void Online_couriers_take_one_order_at_a_time()
    {
        var courier = New();
        courier.GoOnline(Builders.Now);

        courier.TakeOrder(Builders.Now.AddMinutes(1));

        Assert.Equal((CourierStatus.Busy, Builders.Now.AddMinutes(1)), (courier.Status, courier.StatusSince));
        Assert.Equal(ErrorCodes.CourierUnavailable, Assert.Throws<DomainException>(() => courier.TakeOrder(Builders.Now)).Code);
        courier.FinishOrder(Builders.Now.AddMinutes(30));
        Assert.Equal(CourierStatus.Available, courier.Status);
    }

    [Fact]
    public void Offline_couriers_get_nothing_and_busy_ones_cannot_clock_out()
    {
        var courier = New();
        Assert.Throws<DomainException>(() => courier.TakeOrder(Builders.Now));

        courier.GoOnline(Builders.Now);
        courier.TakeOrder(Builders.Now);

        Assert.Throws<DomainException>(() => courier.GoOffline(Builders.Now));
        courier.GoOnline(Builders.Now);
        Assert.Equal(CourierStatus.Busy, courier.Status);
    }

    [Fact]
    public void Old_locations_do_not_overwrite_newer_ones()
    {
        var courier = New();

        courier.MoveTo(GeoPoint.At(41.30, 36.30), Builders.Now.AddSeconds(10));
        courier.MoveTo(GeoPoint.At(41.20, 36.20), Builders.Now.AddSeconds(5));

        Assert.Equal((41.30, Builders.Now.AddSeconds(10)), (courier.Location!.Y, courier.LocationAt!.Value));
    }
}
