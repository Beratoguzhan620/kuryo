using Kuryo.Core.Couriers;
using Kuryo.Core.Geo;
using Kuryo.Core.Orders;
using Kuryo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kuryo.Api.Features.Live;

public sealed record TrackPoint(double Latitude, double Longitude);

/// <summary>The courier as the customer sees them: a first name, how they travel, and where they are while on the way.</summary>
public sealed record TrackCourier(string FirstName, Vehicle Vehicle, double? Latitude, double? Longitude, DateTimeOffset? LocationAt);

/// <summary>
/// What the tracking link shows. Nothing here identifies the customer beyond what they already know: no name, phone,
/// address or notes. The delivery code is shown so the customer can hand it to the courier at the door.
/// </summary>
public sealed record TrackView(
    string Code,
    OrderStatus Status,
    string MerchantName,
    string MerchantPhone,
    TrackPoint Pickup,
    TrackPoint Dropoff,
    DateTimeOffset CreatedAt,
    DateTimeOffset ReadyAt,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? PickedUpAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? EstimatedDeliveryAt,
    TrackCourier? Courier,
    string? DeliveryCode);

internal static class TrackQueries
{
    /// <summary>A link stops working this long after the order is delivered or cancelled.</summary>
    public static readonly TimeSpan LinkLife = TimeSpan.FromHours(24);

    /// <summary>The order behind a tracking token, or null when the token is unknown or its link has ended.</summary>
    public static async Task<(Guid OrderId, TrackView View)?> ByTokenAsync(KuryoDbContext db, string token, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64)
        {
            return null;
        }

        var id = await db.Orders.AsNoTracking().Where(o => o.TrackingToken == token).Select(o => (Guid?)o.Id).SingleOrDefaultAsync(ct);
        if (id is null)
        {
            return null;
        }

        var views = await ForOrdersAsync(db, [id.Value], now, ct);
        return views.TryGetValue(id.Value, out var view) ? (id.Value, view) : null;
    }

    public static async Task<Dictionary<Guid, TrackView>> ForOrdersAsync(KuryoDbContext db, IReadOnlyCollection<Guid> orderIds, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await (
            from o in db.Orders.AsNoTracking()
            join m in db.Merchants.AsNoTracking() on o.MerchantId equals m.Id
            where orderIds.Contains(o.Id)
            select new
            {
                Order = o,
                MerchantName = m.Name,
                MerchantPhone = m.Phone,
                Courier = (
                    from c in db.Couriers
                    join u in db.Users on c.UserId equals u.Id
                    where c.CompanyId == o.CompanyId && c.UserId == o.CourierId
                    select new { Courier = c, u.DisplayName }).FirstOrDefault(),
            }).ToListAsync(ct);

        var views = new Dictionary<Guid, TrackView>();
        foreach (var row in rows)
        {
            var order = row.Order;
            var closedAt = order.Status switch
            {
                OrderStatus.Delivered => order.DeliveredAt,
                OrderStatus.Cancelled => order.UpdatedAt,
                _ => null,
            };
            if (closedAt is { } closed && now - closed > LinkLife)
            {
                continue;
            }

            // The courier's position is shared only while they are on their way with this order.
            var moving = order.Status is OrderStatus.Assigned or OrderStatus.PickedUp;
            var courier = row.Courier is null
                ? null
                : new TrackCourier(
                    FirstName(row.Courier.DisplayName),
                    row.Courier.Courier.Vehicle,
                    moving ? row.Courier.Courier.Location?.Latitude() : null,
                    moving ? row.Courier.Courier.Location?.Longitude() : null,
                    moving ? row.Courier.Courier.LocationAt : null);
            views[order.Id] = new TrackView(
                order.Code,
                order.Status,
                row.MerchantName,
                row.MerchantPhone,
                new TrackPoint(order.PickupLocation.Latitude(), order.PickupLocation.Longitude()),
                new TrackPoint(order.DropoffLocation.Latitude(), order.DropoffLocation.Longitude()),
                order.CreatedAt,
                order.ReadyAt,
                order.AssignedAt,
                order.PickedUpAt,
                order.DeliveredAt,
                order.EstimatedDeliveryAt,
                courier,
                order.IsOpen ? order.DeliveryCode : null);
        }

        return views;
    }

    private static string FirstName(string displayName)
    {
        var trimmed = displayName.Trim();
        var space = trimmed.IndexOf(' ', StringComparison.Ordinal);
        return space > 0 ? trimmed[..space] : trimmed;
    }
}
