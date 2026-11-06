using System.Security.Claims;
using Kuryo.Api.Common;
using Kuryo.Api.Features.Companies;
using Kuryo.Core;
using Kuryo.Core.Companies;
using Kuryo.Core.Geo;
using Kuryo.Core.Merchants;
using Kuryo.Core.Orders;
using Kuryo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Kuryo.Api.Features.Merchants;

public sealed record MerchantRequest(string Name, string? Phone, string Address, double Latitude, double Longitude, int PrepMinutes, bool? Active);

public sealed record MerchantResponse(Guid Id, string Name, string Phone, string Address, double Latitude, double Longitude, int PrepMinutes, bool Active, int OpenOrders, DateTimeOffset CreatedAt);

internal static class MerchantEndpoints
{
    public static IEndpointRouteBuilder MapMerchantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/companies/{companyId:guid}/merchants").WithTags("Merchants").RequireAuthorization();
        group.MapGet("/", List).WithSummary("Merchants, with search by name")
            .WithDescription("Merchant members only see their own.");
        group.MapPost("/", Create).WithSummary("Add a merchant with its pickup address (dispatchers)");
        group.MapGet("/{merchantId:guid}", Get).WithSummary("One merchant");
        group.MapPut("/{merchantId:guid}", Update).WithSummary("Change a merchant, or pause it with `active: false` (dispatchers)");
        return app;
    }

    private static async Task<Ok<List<MerchantResponse>>> List(Guid companyId, string? q, ClaimsPrincipal user, KuryoDbContext db, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireMemberAsync(db, companyId, user, ct);
        var merchants = db.Merchants.AsNoTracking().Where(m => m.CompanyId == companyId);
        if (!membership.IsDispatcher)
        {
            merchants = merchants.Where(m => m.Id == membership.MerchantId);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{Like.Escape(q.Trim())}%";
            merchants = merchants.Where(m => EF.Functions.ILike(m.Name, pattern, "\\"));
        }

        var rows = await merchants.OrderBy(m => m.Name).Take(500)
            .Select(m => new { Merchant = m, Open = db.Orders.Count(o => o.MerchantId == m.Id && o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled) })
            .ToListAsync(ct);
        return TypedResults.Ok(rows.Select(r => ToResponse(r.Merchant, r.Open)).ToList());
    }

    private static async Task<Created<MerchantResponse>> Create(Guid companyId, MerchantRequest request, ClaimsPrincipal user, KuryoDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var merchant = Merchant.Create(companyId, request.Name, request.Phone, request.Address, request.Latitude, request.Longitude, request.PrepMinutes, clock.GetUtcNow());
        if (request.Active == false)
        {
            merchant.Update(request.Name, request.Phone, request.Address, request.Latitude, request.Longitude, request.PrepMinutes, false);
        }

        db.Merchants.Add(merchant);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/companies/{companyId}/merchants/{merchant.Id}", ToResponse(merchant, 0));
    }

    private static async Task<Ok<MerchantResponse>> Get(Guid companyId, Guid merchantId, ClaimsPrincipal user, KuryoDbContext db, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireMemberAsync(db, companyId, user, ct);
        if (!membership.Serves(merchantId) && membership.Role != Role.Courier)
        {
            throw new DomainException(ErrorCodes.NotFound, "There is no merchant with this id.");
        }

        var merchant = await FindAsync(db, companyId, merchantId, ct);
        var open = await db.Orders.CountAsync(o => o.MerchantId == merchantId && o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled, ct);
        return TypedResults.Ok(ToResponse(merchant, open));
    }

    private static async Task<Ok<MerchantResponse>> Update(Guid companyId, Guid merchantId, MerchantRequest request, ClaimsPrincipal user, KuryoDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var merchant = await FindAsync(db, companyId, merchantId, ct);
        merchant.Update(request.Name, request.Phone, request.Address, request.Latitude, request.Longitude, request.PrepMinutes, request.Active ?? merchant.Active);
        await db.SaveChangesAsync(ct);
        var open = await db.Orders.CountAsync(o => o.MerchantId == merchantId && o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled, ct);
        return TypedResults.Ok(ToResponse(merchant, open));
    }

    internal static async Task<Merchant> FindAsync(KuryoDbContext db, Guid companyId, Guid merchantId, CancellationToken ct) =>
        await db.Merchants.SingleOrDefaultAsync(m => m.Id == merchantId && m.CompanyId == companyId, ct)
        ?? throw new DomainException(ErrorCodes.NotFound, "There is no merchant with this id.");

    private static MerchantResponse ToResponse(Merchant m, int open) =>
        new(m.Id, m.Name, m.Phone, m.Address, m.Location.Latitude(), m.Location.Longitude(), m.PrepMinutes, m.Active, open, m.CreatedAt);
}
