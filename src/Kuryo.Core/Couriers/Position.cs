using NetTopologySuite.Geometries;

namespace Kuryo.Core.Couriers;

/// <summary>A courier's position at a moment: the trail behind each delivery, kept for the live map and for disputes.</summary>
public sealed class Position
{
    /// <summary>Positions this far in the past (from a phone that was offline) are dated this far back at most.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    private Position()
    {
    }

    public long Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid CourierId { get; private set; }

    public Point Location { get; private set; } = null!;

    public DateTimeOffset At { get; private set; }

    /// <summary>The order the courier was carrying, if any, so its trail can be drawn.</summary>
    public Guid? OrderId { get; private set; }

    public static Position Record(Guid companyId, Guid courierId, Point location, DateTimeOffset at, Guid? orderId) =>
        new() { CompanyId = companyId, CourierId = courierId, Location = location, At = at, OrderId = orderId };

    /// <summary>A phone's clock, kept between ten minutes ago and now.</summary>
    public static DateTimeOffset Clamp(DateTimeOffset deviceAt, DateTimeOffset now) =>
        deviceAt > now ? now : deviceAt < now - MaxAge ? now - MaxAge : deviceAt;
}
