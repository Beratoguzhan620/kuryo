using System.Security.Claims;
using Kuryo.Api.Common;
using Kuryo.Core.Companies;
using Kuryo.Core.Couriers;
using Kuryo.Core.Orders;
using Kuryo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Kuryo.Api.Features.Companies;

public sealed record CreateCompanyRequest(string Name);

public sealed record CompanySettingsRequest(string Name, int OfferSeconds, double SearchRadiusKm, int MaxOffers);

public sealed record CompanyResponse(
    Guid Id,
    string Name,
    Role Role,
    Guid? MerchantId,
    string TimeZone,
    int OfferSeconds,
    double SearchRadiusKm,
    int MaxOffers,
    int Members,
    int Merchants,
    int CouriersOnline,
    int OpenOrders,
    DateTimeOffset CreatedAt);

internal static class CompanyEndpoints
{
    public static IEndpointRouteBuilder MapCompanyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/companies").WithTags("Companies").RequireAuthorization();
        group.MapGet("/", List).WithSummary("The companies you belong to, with your role in each");
        group.MapPost("/", Create).WithSummary("Create a courier company")
            .WithDescription("You become its owner and can add dispatchers, merchants and couriers.");
        group.MapGet("/{companyId:guid}", Get).WithSummary("One of your companies");
        group.MapPut("/{companyId:guid}", Configure).WithSummary("Rename, and set how offers work (owners)")
            .WithDescription("`offerSeconds`: how long a courier has to accept (10–300). `searchRadiusKm`: how far from the pickup offers go (0.5–50). `maxOffers`: offers per order before a dispatcher steps in (1–20).");
        return app;
    }

    internal static async Task<List<CompanyResponse>> ListForAsync(KuryoDbContext db, Guid userId, CancellationToken ct) =>
        await Query(db, userId, null).ToListAsync(ct);

    private static async Task<Ok<List<CompanyResponse>>> List(ClaimsPrincipal user, KuryoDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await ListForAsync(db, user.UserId(), ct));

    private static async Task<Results<Created<CompanyResponse>, ValidationProblem>> Create(
        CreateCompanyRequest request, ClaimsPrincipal user, KuryoDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var validation = new Validation().Require(
            request.Name?.Trim().Length is >= Company.NameMinLength and <= Company.NameMaxLength,
            "name", $"A name has {Company.NameMinLength} to {Company.NameMaxLength} characters.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var (company, owner) = Company.Create(request.Name!, user.UserId(), clock.GetUtcNow());
        db.Companies.Add(company);
        db.Memberships.Add(owner);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/companies/{company.Id}", await Query(db, user.UserId(), company.Id).SingleAsync(ct));
    }

    private static async Task<Ok<CompanyResponse>> Get(Guid companyId, ClaimsPrincipal user, KuryoDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireMemberAsync(db, companyId, user, ct);
        return TypedResults.Ok(await Query(db, user.UserId(), companyId).SingleAsync(ct));
    }

    private static async Task<Ok<CompanyResponse>> Configure(
        Guid companyId, CompanySettingsRequest request, ClaimsPrincipal user, KuryoDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Owner, ct);
        var company = await db.Companies.SingleAsync(c => c.Id == companyId, ct);
        company.Rename(request.Name);
        company.Configure(request.OfferSeconds, request.SearchRadiusKm, request.MaxOffers);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await Query(db, user.UserId(), companyId).SingleAsync(ct));
    }

    private static IQueryable<CompanyResponse> Query(KuryoDbContext db, Guid userId, Guid? companyId) =>
        from m in db.Memberships
        join c in db.Companies on m.CompanyId equals c.Id
        where m.UserId == userId && (companyId == null || c.Id == companyId)
        orderby c.Name
        select new CompanyResponse(
            c.Id,
            c.Name,
            m.Role,
            m.MerchantId,
            c.TimeZone,
            c.OfferSeconds,
            c.SearchRadiusKm,
            c.MaxOffers,
            db.Memberships.Count(x => x.CompanyId == c.Id),
            db.Merchants.Count(x => x.CompanyId == c.Id),
            db.Couriers.Count(x => x.CompanyId == c.Id && x.Status != CourierStatus.Offline),
            db.Orders.Count(o => o.CompanyId == c.Id && o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled),
            c.CreatedAt);
}
