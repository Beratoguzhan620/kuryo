using Kuryo.Core.Geo;
using NetTopologySuite.Geometries;

namespace Kuryo.Core.Couriers;

public enum Vehicle
{
    Bicycle = 0,
    Scooter = 1,
    Car = 2,
}

public enum CourierStatus
{
    /// <summary>Not working: gets no offers and shares no location.</summary>
    Offline = 0,

    /// <summary>Waiting for work.</summary>
    Available = 1,

    /// <summary>Carrying an order.</summary>
    Busy = 2,
}

/// <summary>A rider: how they travel, whether they are working, and where they were last seen.</summary>
public sealed class Courier
{
    public const int PlateMaxLength = 16;

    /// <summary>A location older than this says little about where the courier is now, so they get no offers until the next one.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(2);

    private Courier()
    {
    }

    public Guid CompanyId { get; private set; }

    public Guid UserId { get; private set; }

    public Vehicle Vehicle { get; private set; }

    public string Plate { get; private set; } = string.Empty;

    public CourierStatus Status { get; private set; }

    public Point? Location { get; private set; }

    public DateTimeOffset? LocationAt { get; private set; }

    public DateTimeOffset StatusSince { get; private set; }

    public static Courier Create(Guid companyId, Guid userId, Vehicle vehicle, string? plate, DateTimeOffset now)
    {
        var courier = new Courier { CompanyId = companyId, UserId = userId, Status = CourierStatus.Offline, StatusSince = now };
        courier.Update(vehicle, plate);
        return courier;
    }

    /// <summary>Average speed in town, used for every estimate.</summary>
    public static double SpeedKmh(Vehicle vehicle) => vehicle switch
    {
        Vehicle.Bicycle => 14,
        Vehicle.Scooter => 24,
        Vehicle.Car => 20,
        _ => throw new DomainException(ErrorCodes.ValidationFailed, "A vehicle is Bicycle, Scooter or Car."),
    };

    public double SpeedKmh() => SpeedKmh(Vehicle);

    public void Update(Vehicle vehicle, string? plate)
    {
        SpeedKmh(vehicle);
        Vehicle = vehicle;
        Plate = string.IsNullOrWhiteSpace(plate) ? string.Empty : Text.Clean(plate, 2, PlateMaxLength, "A plate").ToUpperInvariant();
    }

    /// <summary>Starts or ends a shift. A courier carrying an order finishes it first.</summary>
    public void GoOnline(DateTimeOffset now) => SetStatus(Status == CourierStatus.Busy ? CourierStatus.Busy : CourierStatus.Available, now);

    public void GoOffline(DateTimeOffset now)
    {
        if (Status == CourierStatus.Busy)
        {
            throw new DomainException(ErrorCodes.InvalidState, "Deliver or hand back the order you are carrying first.");
        }

        SetStatus(CourierStatus.Offline, now);
    }

    public void TakeOrder(DateTimeOffset now)
    {
        if (Status != CourierStatus.Available)
        {
            throw new DomainException(ErrorCodes.CourierUnavailable, Status == CourierStatus.Busy ? "This courier is already carrying an order." : "This courier is offline.");
        }

        SetStatus(CourierStatus.Busy, now);
    }

    /// <summary>The order is delivered, cancelled or handed back: free for the next one.</summary>
    public void FinishOrder(DateTimeOffset now)
    {
        if (Status == CourierStatus.Busy)
        {
            SetStatus(CourierStatus.Available, now);
        }
    }

    public bool KnownNear(DateTimeOffset now) => Location is not null && LocationAt is { } at && now - at <= FreshFor;

    public void MoveTo(Point location, DateTimeOffset at)
    {
        if (LocationAt is { } last && at <= last)
        {
            return;
        }

        Location = location;
        LocationAt = at;
    }

    private void SetStatus(CourierStatus status, DateTimeOffset now)
    {
        if (Status != status)
        {
            Status = status;
            StatusSince = now;
        }
    }
}
