using System.Security.Claims;
using Kuryo.Api.Common;
using Kuryo.Api.Features.Auth;
using Kuryo.Core;
using Kuryo.Core.Users;
using Kuryo.Core.Companies;
using Kuryo.Core.Couriers;
using Kuryo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Kuryo.Api.Features.Companies;

public sealed record AddMemberRequest(string Email, Role Role, Guid? MerchantId);

public sealed record ChangeRoleRequest(Role Role, Guid? MerchantId);

public sealed record MemberResponse(Guid UserId, string DisplayName, string Email, Role Role, Guid? MerchantId, string? MerchantName, DateTimeOffset JoinedAt);

internal static class MemberEndpoints
{
    public static IEndpointRouteBuilder MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/companies/{companyId:guid}/members").WithTags("Team").RequireAuthorization();
        group.MapGet("/", List).WithSummary("The team");
        group.MapPost("/", Add).WithSummary("Add someone who has an account (owners)")
            .WithDescription("`role` is `Courier`, `Merchant` (with the `merchantId` they work for), `Dispatcher` or `Owner`. Couriers get a courier profile (scooter) to adjust.");
        group.MapPatch("/{userId:guid}", ChangeRole).WithSummary("Change a member's role (owners)");
        group.MapDelete("/{userId:guid}", Remove).WithSummary("Remove a member (owners), or leave (yourself)")
            .WithDescription("A company always keeps at least one owner.");
        return app;
    }

    private static async Task<Ok<List<MemberResponse>>> List(Guid companyId, ClaimsPrincipal user, KuryoDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireMemberAsync(db, companyId, user, ct);
        var members = await Members(db, companyId, null).ToListAsync(ct);
        return TypedResults.Ok(members.OrderByDescending(m => m.Role).ThenBy(m => m.DisplayName, StringComparer.CurrentCulture).ToList());
    }

    private static async Task<Results<Created<MemberResponse>, ValidationProblem>> Add(
        Guid companyId, AddMemberRequest request, ClaimsPrincipal user, KuryoDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Owner, ct);
        var validation = new Validation()
            .Require(AuthEndpoints.IsEmail(request.Email), "email", "A valid email address is required.")
            .Require(Enum.IsDefined(request.Role), "role", "A role is Courier, Merchant, Dispatcher or Owner.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var email = User.NormalizeEmail(request.Email);
        var invitee = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "Nobody has an account with this email yet. Ask them to sign up, then add them.");
        if (await db.Memberships.AnyAsync(m => m.CompanyId == companyId && m.UserId == invitee.Id, ct))
        {
            throw new DomainException(ErrorCodes.Conflict, "This person is already on the team.");
        }

        await EnsureMerchantAsync(db, companyId, request.MerchantId, ct);
        var now = clock.GetUtcNow();
        var membership = Membership.Create(companyId, invitee.Id, request.Role, request.MerchantId, now);
        db.Memberships.Add(membership);
        if (membership.Role == Role.Courier)
        {
            db.Couriers.Add(Courier.Create(companyId, invitee.Id, Vehicle.Scooter, null, now));
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw new DomainException(ErrorCodes.Conflict, "This person is already on the team.");
        }

        return TypedResults.Created($"/api/companies/{companyId}/members/{invitee.Id}", await Members(db, companyId, invitee.Id).SingleAsync(ct));
    }

    private static async Task<Ok<MemberResponse>> ChangeRole(
        Guid companyId, Guid userId, ChangeRoleRequest request, ClaimsPrincipal user, KuryoDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Owner, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await CompanyAccess.LockAsync(db, companyId, ct);
        var membership = await db.Memberships.SingleOrDefaultAsync(m => m.CompanyId == companyId && m.UserId == userId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "This person is not on the team.");

        var owners = await db.Memberships.CountAsync(m => m.CompanyId == companyId && m.Role == Role.Owner, ct);
        var wasOwner = membership.Role == Role.Owner;
        await EnsureMerchantAsync(db, companyId, request.MerchantId, ct);
        var wasCourier = membership.Role == Role.Courier;
        membership.ChangeRole(request.Role, request.MerchantId);
        var courier = await db.Couriers.SingleOrDefaultAsync(c => c.CompanyId == companyId && c.UserId == userId, ct);
        if (membership.Role == Role.Courier && courier is null)
        {
            db.Couriers.Add(Courier.Create(companyId, userId, Vehicle.Scooter, null, clock.GetUtcNow()));
        }
        else if (wasCourier && membership.Role != Role.Courier && courier is not null)
        {
            if (courier.Status == CourierStatus.Busy)
            {
                throw new DomainException(ErrorCodes.InvalidState, "This courier is carrying an order. Let them finish it first.");
            }

            db.Couriers.Remove(courier);
        }

        Company.EnsureAnOwnerRemains(owners - (wasOwner ? 1 : 0) + (membership.Role == Role.Owner ? 1 : 0));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.Ok(await Members(db, companyId, userId).SingleAsync(ct));
    }

    private static async Task<NoContent> Remove(
        Guid companyId, Guid userId, ClaimsPrincipal user, KuryoDbContext db, CancellationToken ct)
    {
        // Anyone may leave; removing someone else takes an owner.
        if (userId == user.UserId())
        {
            await CompanyAccess.RequireMemberAsync(db, companyId, user, ct);
        }
        else
        {
            await CompanyAccess.RequireAsync(db, companyId, user, Role.Owner, ct);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await CompanyAccess.LockAsync(db, companyId, ct);
        var membership = await db.Memberships.SingleOrDefaultAsync(m => m.CompanyId == companyId && m.UserId == userId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "This person is not on the team.");
        if (membership.Role == Role.Owner)
        {
            var owners = await db.Memberships.CountAsync(m => m.CompanyId == companyId && m.Role == Role.Owner, ct);
            Company.EnsureAnOwnerRemains(owners - 1);
        }

        if (await db.Couriers.AnyAsync(c => c.CompanyId == companyId && c.UserId == userId && c.Status == CourierStatus.Busy, ct))
        {
            throw new DomainException(ErrorCodes.InvalidState, "This courier is carrying an order. Let them finish it first.");
        }

        db.Memberships.Remove(membership);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task EnsureMerchantAsync(KuryoDbContext db, Guid companyId, Guid? merchantId, CancellationToken ct)
    {
        if (merchantId is { } id && !await db.Merchants.AnyAsync(x => x.Id == id && x.CompanyId == companyId, ct))
        {
            throw new DomainException(ErrorCodes.NotFound, "There is no merchant with this id.");
        }
    }

    private static IQueryable<MemberResponse> Members(KuryoDbContext db, Guid companyId, Guid? userId) =>
        from m in db.Memberships
        join u in db.Users on m.UserId equals u.Id
        where m.CompanyId == companyId && (userId == null || m.UserId == userId)
        select new MemberResponse(u.Id, u.DisplayName, u.Email, m.Role, m.MerchantId, db.Merchants.Where(x => x.Id == m.MerchantId).Select(x => x.Name).FirstOrDefault(), m.JoinedAt);
}
