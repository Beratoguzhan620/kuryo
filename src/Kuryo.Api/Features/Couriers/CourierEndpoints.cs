using System.Security.Claims;
using Kuryo.Api.Features.Companies;
using Kuryo.Api.Features.Matching;
using Kuryo.Core;
using Kuryo.Core.Companies;
using Kuryo.Core.Couriers;
using Kuryo.Core.Geo;
using Kuryo.Core.Orders;
using Kuryo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Kuryo.Api.Features.Couriers;

public sealed record CourierRequest(Vehicle Vehicle, string? Plate);

public sealed record StatusRequest(bool Online);

public sealed record PingPoint(double Latitude, double Longitude, DateTimeOffset At);

/// <summary>Positions the phone collected since its last upload, oldest first; a phone that was offline sends its backlog in one go.</summary>
public sealed record PingRequest(IReadOnlyList<PingPoint> Points);

public sealed record PingResponse(int Stored, DateTimeOffset? LocationAt);

public sealed record TrailPoint(double Latitude, double Longitude, DateTimeOffset At);

public sealed record CourierResponse(
    Guid UserId,
    string DisplayName,
    Vehicle Vehicle,
    string Plate,
    CourierStatus Status,
    DateTimeOffset StatusSince,
    double? Latitude,
    double? Longitude,
    DateTimeOffset? LocationAt,
    Guid? OrderId,
    string? OrderCode);

internal static class CourierEndpoints
{
    public static IEndpointRouteBuilder MapCourierEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/companies/{companyId:guid}/couriers").WithTags("Couriers").RequireAuthorization();
        group.MapGet("/", List).WithSummary("Couriers with their status, last location and current order (dispatchers)");
        group.MapPut("/{userId:guid}", Update).WithSummary("Set a courier's vehicle and plate (dispatchers)")
            .WithDescription("The vehicle sets the speed used for every estimate: bicycle 14 km/h, scooter 24, car 20 in town traffic.");
        group.MapGet("/me", Me).WithSummary("Your own courier profile");
        group.MapPost("/me/status", SetStatus).WithSummary("Go online for offers, or offline")
            .WithDescription("A courier carrying an order finishes it before going offline. Going offline takes back an open offer.");
        group.MapPost("/me/locations", Ping).WithSummary("Upload the positions your phone collected")
            .WithDescription($"Up to {MaxPoints} points a request. Times from the phone are kept between ten minutes ago and now; the newest point becomes your location for offers and the live map.");
        group.MapGet("/{userId:guid}/trail", Trail).WithSummary("Where a courier has been in the last hours, or along one order (dispatchers)");
        return app;
    }

    private static async Task<Ok<List<CourierResponse>>> List(Guid companyId, ClaimsPrincipal user, KuryoDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        return TypedResults.Ok(await QueryAsync(db, companyId, null, ct));
    }

    private static async Task<Ok<CourierResponse>> Update(Guid companyId, Guid userId, CourierRequest request, ClaimsPrincipal user, KuryoDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var courier = await FindAsync(db, companyId, userId, ct);
        courier.Update(request.Vehicle, request.Plate);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok((await QueryAsync(db, companyId, userId, ct)).Single());
    }

    private static async Task<Ok<CourierResponse>> Me(Guid companyId, ClaimsPrincipal user, KuryoDbContext db, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Courier, ct);
        await FindAsync(db, companyId, membership.UserId, ct);
        return TypedResults.Ok((await QueryAsync(db, companyId, membership.UserId, ct)).Single());
    }

    private static async Task<Ok<CourierResponse>> SetStatus(Guid companyId, StatusRequest request, ClaimsPrincipal user, KuryoDbContext db, TimeProvider clock, MatchingSignal signal, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Courier, ct);
        var courier = await FindAsync(db, companyId, membership.UserId, ct);
        var now = clock.GetUtcNow();
        if (request.Online)
        {
            courier.GoOnline(now);
            await db.SaveChangesAsync(ct);
            signal.Poke();
        }
        else
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            courier.GoOffline(now);
            // An offer nobody will answer only keeps the order waiting.
            await OfferEndpoints.WithdrawForCourierAsync(db, membership.UserId, now, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            signal.Poke();
        }

        return TypedResults.Ok((await QueryAsync(db, companyId, membership.UserId, ct)).Single());
    }

    public const int MaxPoints = 50;

    private static async Task<Ok<PingResponse>> Ping(Guid companyId, PingRequest request, ClaimsPrincipal user, KuryoDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Courier, ct);
        if (request.Points is not { Count: > 0 and <= MaxPoints })
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"Send between 1 and {MaxPoints} points.");
        }

        var courier = await FindAsync(db, companyId, membership.UserId, ct);
        if (courier.Status == CourierStatus.Offline)
        {
            throw new DomainException(ErrorCodes.InvalidState, "Go online to share your location; offline, it stays on your phone.");
        }

        var now = clock.GetUtcNow();
        var carrying = await db.Orders
            .Where(o => o.CompanyId == companyId && o.CourierId == membership.UserId && (o.Status == OrderStatus.Assigned || o.Status == OrderStatus.PickedUp))
            .Select(o => (Guid?)o.Id)
            .FirstOrDefaultAsync(ct);
        foreach (var point in request.Points.OrderBy(p => p.At))
        {
            var location = GeoPoint.At(point.Latitude, point.Longitude);
            var at = Position.Clamp(point.At, now);
            db.Positions.Add(Position.Record(companyId, membership.UserId, location, at, carrying));
            courier.MoveTo(location, at);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new PingResponse(request.Points.Count, courier.LocationAt));
    }

    private static async Task<Ok<List<TrailPoint>>> Trail(Guid companyId, Guid userId, Guid? orderId, ClaimsPrincipal user, KuryoDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var since = clock.GetUtcNow().AddHours(-3);
        var points = await db.Positions.AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.CourierId == userId && (orderId == null ? p.At >= since : p.OrderId == orderId))
            .OrderBy(p => p.At)
            .Take(2000)
            .Select(p => new { p.Location, p.At })
            .ToListAsync(ct);
        return TypedResults.Ok(points.Select(p => new TrailPoint(p.Location.Latitude(), p.Location.Longitude(), p.At)).ToList());
    }

    internal static async Task<Courier> FindAsync(KuryoDbContext db, Guid companyId, Guid userId, CancellationToken ct) =>
        await db.Couriers.SingleOrDefaultAsync(c => c.CompanyId == companyId && c.UserId == userId, ct)
        ?? throw new DomainException(ErrorCodes.NotFound, "There is no courier with this id.");

    private static async Task<List<CourierResponse>> QueryAsync(KuryoDbContext db, Guid companyId, Guid? userId, CancellationToken ct)
    {
        var rows = await (
            from c in db.Couriers.AsNoTracking()
            join u in db.Users on c.UserId equals u.Id
            where c.CompanyId == companyId && (userId == null || c.UserId == userId)
            orderby u.DisplayName
            select new
            {
                Courier = c,
                u.DisplayName,
                Current = db.Orders
                    .Where(o => o.CompanyId == companyId && o.CourierId == c.UserId && (o.Status == OrderStatus.Assigned || o.Status == OrderStatus.PickedUp))
                    .OrderBy(o => o.AssignedAt)
                    .Select(o => new { o.Id, o.Number })
                    .FirstOrDefault(),
            }).ToListAsync(ct);
        return rows.Select(r => new CourierResponse(
            r.Courier.UserId,
            r.DisplayName,
            r.Courier.Vehicle,
            r.Courier.Plate,
            r.Courier.Status,
            r.Courier.StatusSince,
            r.Courier.Location?.Latitude(),
            r.Courier.Location?.Longitude(),
            r.Courier.LocationAt,
            r.Current?.Id,
            r.Current is null ? null : Order.Format(r.Current.Number))).ToList();
    }
}
