using Kuryo.Core.Companies;
using Kuryo.Core.Geo;
using Kuryo.Core.Offers;
using Kuryo.Core.Orders;
using Kuryo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kuryo.Api.Features.Orders;

internal static class OrderQueries
{
    public static async Task<List<OrderSummary>> SummariesAsync(KuryoDbContext db, IQueryable<Order> orders, CancellationToken ct)
    {
        var rows = await (
            from o in orders
            join m in db.Merchants on o.MerchantId equals m.Id
            select new
            {
                Order = o,
                MerchantName = m.Name,
                CourierName = db.Users.Where(u => u.Id == o.CourierId).Select(u => u.DisplayName).FirstOrDefault(),
            }).ToListAsync(ct);
        return rows.Select(r => new OrderSummary(
            r.Order.Id, r.Order.Code, r.Order.Status, r.Order.MerchantId, r.MerchantName, r.Order.CustomerName, r.Order.DropoffAddress,
            r.Order.DropoffLocation.Latitude(), r.Order.DropoffLocation.Longitude(), r.Order.CourierId, r.CourierName, r.Order.OffersMade,
            r.Order.ReadyAt, r.Order.EstimatedDeliveryAt, r.Order.CreatedAt)).ToList();
    }

    public static async Task<OrderResponse> DetailAsync(KuryoDbContext db, Guid orderId, Membership viewer, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId, ct);
        var merchant = await db.Merchants.AsNoTracking().SingleAsync(m => m.Id == order.MerchantId, ct);
        var zone = await db.Zones.AsNoTracking().Where(z => z.Id == order.ZoneId).Select(z => z.Name).SingleAsync(ct);
        CourierRef? courier = null;
        if (order.CourierId is { } courierId)
        {
            var c = await (
                from x in db.Couriers.AsNoTracking()
                join u in db.Users on x.UserId equals u.Id
                where x.CompanyId == order.CompanyId && x.UserId == courierId
                select new { Courier = x, u.DisplayName }).SingleOrDefaultAsync(ct);
            courier = c is null
                ? null
                : new CourierRef(courierId, c.DisplayName, c.Courier.Vehicle, c.Courier.Plate, c.Courier.Location?.Latitude(), c.Courier.Location?.Longitude(), c.Courier.LocationAt);
        }

        var pending = await (
            from f in db.Offers.AsNoTracking()
            join u in db.Users on f.CourierId equals u.Id
            where f.OrderId == orderId && f.Status == OfferStatus.Pending
            select new OfferRef(f.CourierId, u.DisplayName, f.DistanceKm, f.ExpiresAt)).SingleOrDefaultAsync(ct);

        var history = await (
            from e in db.OrderEvents.AsNoTracking()
            where e.OrderId == orderId
            orderby e.Id
            select new OrderEventResponse(e.Id, e.Kind, db.Users.Where(u => u.Id == e.ActorId).Select(u => u.DisplayName).FirstOrDefault(), e.At, e.Detail))
            .ToListAsync(ct);

        // The courier has to ask for the code at the door; showing it to them would defeat it.
        var secrets = viewer.Serves(order.MerchantId);
        return new OrderResponse(
            order.Id,
            order.Number,
            order.Code,
            order.Status,
            new MerchantRef(merchant.Id, merchant.Name, merchant.Phone),
            new PlaceRef(order.PickupAddress, order.PickupLocation.Latitude(), order.PickupLocation.Longitude()),
            order.CustomerName,
            order.CustomerPhone,
            new PlaceRef(order.DropoffAddress, order.DropoffLocation.Latitude(), order.DropoffLocation.Longitude()),
            order.DropoffNotes,
            order.Items,
            zone,
            order.DeliveryFeeMinor / 100m,
            order.DistanceKm,
            order.ReadyAt,
            courier,
            order.OffersMade,
            pending,
            order.AssignedAt,
            order.PickedUpAt,
            order.DeliveredAt,
            order.EstimatedDeliveryAt,
            order.CancelReason,
            secrets ? order.TrackingToken : null,
            secrets ? order.DeliveryCode : null,
            order.CodeAttempts,
            history,
            order.CreatedAt,
            order.UpdatedAt);
    }
}
