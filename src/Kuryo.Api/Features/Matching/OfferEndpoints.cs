using System.Security.Claims;
using Kuryo.Api.Features.Companies;
using Kuryo.Api.Features.Couriers;
using Kuryo.Api.Features.Orders;
using Kuryo.Core;
using Kuryo.Core.Companies;
using Kuryo.Core.Geo;
using Kuryo.Core.Offers;
using Kuryo.Core.Orders;
using Kuryo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Kuryo.Api.Features.Matching;

/// <summary>
/// What a courier sees before saying yes: where to collect, roughly where to go, the fee and the clock. The
/// customer's name and phone come only with the order itself, after accepting.
/// </summary>
public sealed record OfferResponse(
    Guid Id,
    Guid OrderId,
    string OrderCode,
    string MerchantName,
    PlaceRef Pickup,
    PlaceRef Dropoff,
    double ToPickupKm,
    double TripKm,
    decimal DeliveryFee,
    DateTimeOffset ReadyAt,
    DateTimeOffset OfferedAt,
    DateTimeOffset ExpiresAt);

internal static class OfferEndpoints
{
    public static IEndpointRouteBuilder MapOfferEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/companies/{companyId:guid}").WithTags("Offers").RequireAuthorization();
        group.MapGet("/couriers/me/offer", Current).WithSummary("The order offered to you right now, if any")
            .WithDescription("204 when there is nothing to answer. The offer runs out at expiresAt; after that it goes to the next courier.");
        group.MapPost("/offers/{offerId:guid}/accept", Accept).WithSummary("Take the offered order");
        group.MapPost("/offers/{offerId:guid}/decline", Decline).WithSummary("Pass on the offered order; it goes to the next nearest courier");
        return app;
    }

    private static async Task<Results<Ok<OfferResponse>, NoContent>> Current(Guid companyId, ClaimsPrincipal user, KuryoDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Courier, ct);
        var now = clock.GetUtcNow();
        var row = await (
            from f in db.Offers.AsNoTracking()
            join o in db.Orders.AsNoTracking() on f.OrderId equals o.Id
            join m in db.Merchants.AsNoTracking() on o.MerchantId equals m.Id
            where f.CompanyId == companyId && f.CourierId == membership.UserId && f.Status == OfferStatus.Pending && f.ExpiresAt > now
            select new { Offer = f, Order = o, MerchantName = m.Name }).SingleOrDefaultAsync(ct);
        if (row is null)
        {
            return TypedResults.NoContent();
        }

        var (offer, order) = (row.Offer, row.Order);
        return TypedResults.Ok(new OfferResponse(
            offer.Id, order.Id, order.Code, row.MerchantName,
            new PlaceRef(order.PickupAddress, order.PickupLocation.Latitude(), order.PickupLocation.Longitude()),
            new PlaceRef(order.DropoffAddress, order.DropoffLocation.Latitude(), order.DropoffLocation.Longitude()),
            offer.DistanceKm, order.DistanceKm, order.DeliveryFeeMinor / 100m, order.ReadyAt, offer.OfferedAt, offer.ExpiresAt));
    }

    private static async Task<Ok<OrderResponse>> Accept(Guid companyId, Guid offerId, ClaimsPrincipal user, KuryoDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Courier, ct);
        var offer = await FindAsync(db, companyId, offerId, membership, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Courier, then order, then the offer: the order every path locks in.
        await OrderEndpoints.LockCourierAsync(db, companyId, membership.UserId, ct);
        await OrderEndpoints.LockOrderAsync(db, companyId, offer.OrderId, ct);
        await db.Entry(offer).ReloadAsync(ct);
        var now = clock.GetUtcNow();
        offer.Accept(now);
        var courier = await CourierEndpoints.FindAsync(db, companyId, membership.UserId, ct);
        var order = await db.Orders.SingleAsync(o => o.Id == offer.OrderId, ct);
        courier.TakeOrder(now);
        var toPickup = courier.Location is null ? offer.DistanceKm : GeoPoint.DistanceKm(courier.Location, order.PickupLocation);
        order.Assign(courier.UserId, courier.SpeedKmh(), toPickup, now);
        var name = await db.Users.Where(u => u.Id == courier.UserId).Select(u => u.DisplayName).SingleAsync(ct);
        db.OrderEvents.Add(OrderEvent.Record(order, OrderEventKind.Assigned, membership.UserId, now, $"{name} accepted the offer"));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.Ok(await OrderQueries.DetailAsync(db, order.Id, membership, ct));
    }

    private static async Task<NoContent> Decline(Guid companyId, Guid offerId, ClaimsPrincipal user, KuryoDbContext db, TimeProvider clock, MatchingSignal signal, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Courier, ct);
        var offer = await FindAsync(db, companyId, offerId, membership, ct);
        var now = clock.GetUtcNow();
        offer.Decline(now);
        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == offer.OrderId, ct);
        var name = await db.Users.Where(u => u.Id == membership.UserId).Select(u => u.DisplayName).SingleAsync(ct);
        db.OrderEvents.Add(OrderEvent.Record(order, OrderEventKind.OfferDeclined, membership.UserId, now, $"{name} passed"));
        await db.SaveChangesAsync(ct);
        signal.Poke();
        return TypedResults.NoContent();
    }

    /// <summary>Only the courier it was made to can see or answer an offer.</summary>
    private static async Task<Offer> FindAsync(KuryoDbContext db, Guid companyId, Guid offerId, Membership membership, CancellationToken ct) =>
        await db.Offers.SingleOrDefaultAsync(f => f.Id == offerId && f.CompanyId == companyId && f.CourierId == membership.UserId, ct)
        ?? throw new DomainException(ErrorCodes.NotFound, "There is no offer with this id.");

    /// <summary>Takes back any open offer for an order (assigned by hand, cancelled) so nobody accepts it late.</summary>
    internal static Task WithdrawForOrderAsync(KuryoDbContext db, Guid orderId, DateTimeOffset now, CancellationToken ct) =>
        db.Offers.Where(f => f.OrderId == orderId && f.Status == OfferStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Status, OfferStatus.Withdrawn).SetProperty(f => f.RespondedAt, now), ct);

    /// <summary>Takes back the open offer a courier has (they went offline or were given another order).</summary>
    internal static Task WithdrawForCourierAsync(KuryoDbContext db, Guid courierId, DateTimeOffset now, CancellationToken ct) =>
        db.Offers.Where(f => f.CourierId == courierId && f.Status == OfferStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Status, OfferStatus.Withdrawn).SetProperty(f => f.RespondedAt, now), ct);
}
