using System.Security.Claims;
using Kuryo.Api.Features.Companies;
using Kuryo.Core;
using Kuryo.Core.Companies;
using Kuryo.Core.Geo;
using Kuryo.Core.Zones;
using Kuryo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Kuryo.Api.Features.Zones;

/// <summary><c>corners</c> are [latitude, longitude] pairs, in order around the area.</summary>
public sealed record ZoneRequest(string Name, IReadOnlyList<double[]> Corners, decimal DeliveryFee, bool? Active);

public sealed record ZoneResponse(Guid Id, string Name, IReadOnlyList<double[]> Corners, decimal DeliveryFee, bool Active, double AreaKm2);

public sealed record ZoneCheckResponse(bool Delivers, Guid? ZoneId, string? ZoneName, decimal? DeliveryFee);

internal static class ZoneEndpoints
{
    public static IEndpointRouteBuilder MapZoneEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/companies/{companyId:guid}/zones").WithTags("Zones").RequireAuthorization();
        group.MapGet("/", List).WithSummary("Delivery zones with their areas and fees");
        group.MapPost("/", Create).WithSummary("Draw a delivery zone (dispatchers)")
            .WithDescription("`corners` are 3 to 200 [latitude, longitude] pairs in order around the area; edges may not cross. Zones may overlap: the cheapest active one applies.");
        group.MapPut("/{zoneId:guid}", Update).WithSummary("Change a zone, or switch it off with `active: false` (dispatchers)");
        group.MapGet("/check", Check).WithSummary("Whether the company delivers to a point, and for how much");
        return app;
    }

    private static async Task<Ok<List<ZoneResponse>>> List(Guid companyId, ClaimsPrincipal user, KuryoDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireMemberAsync(db, companyId, user, ct);
        var zones = await db.Zones.AsNoTracking().Where(z => z.CompanyId == companyId).OrderBy(z => z.Name)
            .Select(z => new { Zone = z, Area = z.Area.Area })
            .ToListAsync(ct);
        return TypedResults.Ok(zones.Select(z => ToResponse(z.Zone, z.Area)).ToList());
    }

    private static async Task<Created<ZoneResponse>> Create(Guid companyId, ZoneRequest request, ClaimsPrincipal user, KuryoDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var zone = Zone.Create(companyId, request.Name, request.Corners ?? [], request.DeliveryFee, clock.GetUtcNow());
        if (request.Active == false)
        {
            zone.Update(request.Name, request.Corners ?? [], request.DeliveryFee, false);
        }

        db.Zones.Add(zone);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/companies/{companyId}/zones/{zone.Id}", await LoadAsync(db, zone.Id, ct));
    }

    private static async Task<Ok<ZoneResponse>> Update(Guid companyId, Guid zoneId, ZoneRequest request, ClaimsPrincipal user, KuryoDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var zone = await db.Zones.SingleOrDefaultAsync(z => z.Id == zoneId && z.CompanyId == companyId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no zone with this id.");
        zone.Update(request.Name, request.Corners ?? [], request.DeliveryFee, request.Active ?? zone.Active);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await LoadAsync(db, zone.Id, ct));
    }

    private static async Task<Ok<ZoneCheckResponse>> Check(Guid companyId, double lat, double lng, ClaimsPrincipal user, KuryoDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireMemberAsync(db, companyId, user, ct);
        var zone = await ZoneFor(db, companyId, GeoPoint.At(lat, lng), ct);
        return TypedResults.Ok(zone is null ? new ZoneCheckResponse(false, null, null, null) : new ZoneCheckResponse(true, zone.Id, zone.Name, zone.DeliveryFeeMinor / 100m));
    }

    /// <summary>The cheapest active zone that covers a point, found by PostGIS on the area index.</summary>
    internal static Task<Zone?> ZoneFor(KuryoDbContext db, Guid companyId, Point point, CancellationToken ct) =>
        db.Zones.Where(z => z.CompanyId == companyId && z.Active && z.Area.Covers(point))
            .OrderBy(z => z.DeliveryFeeMinor).ThenBy(z => z.Name)
            .FirstOrDefaultAsync(ct);

    private static async Task<ZoneResponse> LoadAsync(KuryoDbContext db, Guid zoneId, CancellationToken ct)
    {
        var row = await db.Zones.AsNoTracking().Where(z => z.Id == zoneId).Select(z => new { Zone = z, Area = z.Area.Area }).SingleAsync(ct);
        return ToResponse(row.Zone, row.Area);
    }

    // On geography, PostGIS gives the area in square metres.
    private static ZoneResponse ToResponse(Zone z, double areaM2) =>
        new(z.Id, z.Name, z.Corners, z.DeliveryFeeMinor / 100m, z.Active, Math.Round(areaM2 / 1_000_000, 2));
}
