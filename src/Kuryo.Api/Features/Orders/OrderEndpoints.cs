using System.Security.Claims;
using Kuryo.Api.Common;
using Kuryo.Api.Features.Companies;
using Kuryo.Api.Features.Couriers;
using Kuryo.Api.Features.Matching;
using Kuryo.Api.Features.Merchants;
using Kuryo.Api.Features.Zones;
using Kuryo.Core;
using Kuryo.Core.Companies;
using Kuryo.Core.Couriers;
using Kuryo.Core.Geo;
using Kuryo.Core.Orders;
using Kuryo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Kuryo.Api.Features.Orders;

internal static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/companies/{companyId:guid}/orders").WithTags("Orders").RequireAuthorization();
        group.MapGet("/", List).WithSummary("Orders, newest first, filtered and paged")
            .WithDescription("Filters combine: `status` (repeat for several), `merchantId`, `courierId`, `q` (code or customer). Merchants see their own merchant's orders; couriers the ones they carry or carried.");
        group.MapPost("/", Create).WithSummary("Send an order (merchants for their own merchant, dispatchers for any)")
            .WithDescription("The drop-off has to be inside an active zone; its fee applies. The order starts searching for a courier straight away.");
        group.MapGet("/{orderId:guid}", Get).WithSummary("One order with its history");
        group.MapPost("/{orderId:guid}/assign", Assign).WithSummary("Give the order to an available courier (dispatchers)");
        group.MapPost("/{orderId:guid}/release", Release).WithSummary("Hand the order back before pickup (its courier or a dispatcher); it searches again");
        group.MapPost("/{orderId:guid}/search-again", SearchAgain).WithSummary("Send offers again for an order nobody took (dispatchers)");
        group.MapPost("/{orderId:guid}/pickup", PickUp).WithSummary("The courier has the order");
        group.MapPost("/{orderId:guid}/deliver", Deliver).WithSummary("Finish with the customer's 4-digit code (the courier)")
            .WithDescription("Five wrong codes lock the order; a dispatcher can then confirm the delivery.");
        group.MapPost("/{orderId:guid}/confirm-delivered", ConfirmDelivered).WithSummary("Close a picked-up order without the code (dispatchers)");
        group.MapPost("/{orderId:guid}/cancel", Cancel).WithSummary("Cancel: merchants before pickup, dispatchers until delivery");
        return app;
    }

    private static async Task<Results<Ok<Page<OrderSummary>>, ValidationProblem>> List(
        Guid companyId, string[]? status, Guid? merchantId, Guid? courierId, string? q, int? page, int? pageSize,
        ClaimsPrincipal user, KuryoDbContext db, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireMemberAsync(db, companyId, user, ct);
        var statuses = new List<OrderStatus>();
        var validation = new Validation();
        foreach (var part in (status ?? []).SelectMany(s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            validation.Require(Enum.TryParse<OrderStatus>(part, true, out var parsed) && Enum.IsDefined(parsed), "status", $"'{part}' is not a status.");
            statuses.Add(parsed);
        }

        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var (number, size) = Paging.Clamp(page, pageSize);
        var orders = db.Orders.AsNoTracking().Where(o => o.CompanyId == companyId);
        orders = membership.Role switch
        {
            Role.Merchant => orders.Where(o => o.MerchantId == membership.MerchantId),
            Role.Courier => orders.Where(o => o.CourierId == membership.UserId),
            _ => orders,
        };
        if (statuses.Count > 0)
        {
            orders = orders.Where(o => statuses.Contains(o.Status));
        }

        if (merchantId is { } m)
        {
            orders = orders.Where(o => o.MerchantId == m);
        }

        if (courierId is { } c)
        {
            orders = orders.Where(o => o.CourierId == c);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var text = q.Trim();
            var pattern = $"%{Like.Escape(text)}%";
            var digits = text.StartsWith("KR-", StringComparison.OrdinalIgnoreCase) ? text[3..] : text;
            var byNumber = int.TryParse(digits, out var n) ? n : -1;
            orders = orders.Where(o => o.Number == byNumber || EF.Functions.ILike(o.CustomerName, pattern, "\\") || EF.Functions.ILike(o.DropoffAddress, pattern, "\\"));
        }

        var total = await orders.CountAsync(ct);
        var items = await OrderQueries.SummariesAsync(db, orders.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Number).Skip((number - 1) * size).Take(size), ct);
        return TypedResults.Ok(new Page<OrderSummary>(items, total, number, size));
    }

    private static async Task<Created<OrderResponse>> Create(Guid companyId, CreateOrderRequest request, ClaimsPrincipal user, KuryoDbContext db, TimeProvider clock, MatchingSignal signal, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Merchant, ct);
        if (!membership.Serves(request.MerchantId))
        {
            throw new DomainException(ErrorCodes.Forbidden, "You can only send orders for your own merchant.");
        }

        var merchant = await MerchantEndpoints.FindAsync(db, companyId, request.MerchantId, ct);
        var zone = await ZoneEndpoints.ZoneFor(db, companyId, GeoPoint.At(request.Latitude, request.Longitude), ct)
            ?? throw new DomainException(ErrorCodes.OutsideZone, "The address is outside the areas the company delivers to.");

        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var number = (await db.Database
            .SqlQuery<int>($"UPDATE companies SET next_order_number = next_order_number + 1 WHERE id = {companyId} RETURNING next_order_number - 1 AS \"Value\"")
            .ToListAsync(ct)).Single();
        var order = Order.Create(companyId, number, merchant, zone, request.CustomerName, request.CustomerPhone, request.DropoffAddress,
            request.Latitude, request.Longitude, request.DropoffNotes, request.Items, membership.UserId, now);
        db.Orders.Add(order);
        db.OrderEvents.Add(OrderEvent.Record(order, OrderEventKind.Created, membership.UserId, now, $"{zone.Name}, {order.DistanceKm:0.0} km"));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        signal.Poke();
        return TypedResults.Created($"/api/companies/{companyId}/orders/{order.Id}", await OrderQueries.DetailAsync(db, order.Id, membership, ct));
    }

    private static async Task<Ok<OrderResponse>> Get(Guid companyId, Guid orderId, ClaimsPrincipal user, KuryoDbContext db, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireMemberAsync(db, companyId, user, ct);
        await FindAsync(db, companyId, orderId, membership, ct);
        return TypedResults.Ok(await OrderQueries.DetailAsync(db, orderId, membership, ct));
    }

    private static async Task<Ok<OrderResponse>> Assign(Guid companyId, Guid orderId, AssignRequest request, ClaimsPrincipal user, KuryoDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Courier first, then order: the same order every path takes, so two assignments cannot deadlock.
        await LockCourierAsync(db, companyId, request.CourierId, ct);
        await LockOrderAsync(db, companyId, orderId, ct);
        var order = await FindAsync(db, companyId, orderId, membership, ct);
        var courier = await CourierEndpoints.FindAsync(db, companyId, request.CourierId, ct);
        var now = clock.GetUtcNow();
        courier.TakeOrder(now);
        var toPickup = courier.Location is null ? 0 : GeoPoint.DistanceKm(courier.Location, order.PickupLocation);
        order.Assign(courier.UserId, courier.SpeedKmh(), toPickup, now);
        // An offer still out for this order, or one this courier was thinking about, no longer stands.
        await OfferEndpoints.WithdrawForOrderAsync(db, orderId, now, ct);
        await OfferEndpoints.WithdrawForCourierAsync(db, courier.UserId, now, ct);
        var name = await db.Users.Where(u => u.Id == courier.UserId).Select(u => u.DisplayName).SingleAsync(ct);
        db.OrderEvents.Add(OrderEvent.Record(order, OrderEventKind.Assigned, membership.UserId, now, $"{name}, by a dispatcher"));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.Ok(await OrderQueries.DetailAsync(db, orderId, membership, ct));
    }

    private static async Task<Ok<OrderResponse>> Release(Guid companyId, Guid orderId, ReasonRequest request, ClaimsPrincipal user, KuryoDbContext db, TimeProvider clock, MatchingSignal signal, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireMemberAsync(db, companyId, user, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await FindAsync(db, companyId, orderId, membership, ct);
        if (!membership.IsDispatcher && order.CourierId != membership.UserId)
        {
            throw new DomainException(ErrorCodes.Forbidden, "Only the order's courier or a dispatcher can hand it back.");
        }

        if (order.CourierId is { } carrier)
        {
            await LockCourierAsync(db, companyId, carrier, ct);
        }

        await LockOrderAsync(db, companyId, orderId, ct);
        await db.Entry(order).ReloadAsync(ct);
        var now = clock.GetUtcNow();
        var courierId = order.Release(now);
        (await CourierEndpoints.FindAsync(db, companyId, courierId, ct)).FinishOrder(now);
        db.OrderEvents.Add(OrderEvent.Record(order, OrderEventKind.Released, membership.UserId, now, Text.Clean(request.Reason, 3, 300, "A reason")));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        signal.Poke();
        return TypedResults.Ok(await OrderQueries.DetailAsync(db, orderId, membership, ct));
    }

    private static async Task<Ok<OrderResponse>> SearchAgain(Guid companyId, Guid orderId, ClaimsPrincipal user, KuryoDbContext db, TimeProvider clock, MatchingSignal signal, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var order = await FindAsync(db, companyId, orderId, membership, ct);
        var now = clock.GetUtcNow();
        order.SearchAgain(now);
        db.OrderEvents.Add(OrderEvent.Record(order, OrderEventKind.SearchAgain, membership.UserId, now));
        await db.SaveChangesAsync(ct);
        signal.Poke();
        return TypedResults.Ok(await OrderQueries.DetailAsync(db, orderId, membership, ct));
    }

    private static async Task<Ok<OrderResponse>> PickUp(Guid companyId, Guid orderId, ClaimsPrincipal user, KuryoDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Courier, ct);
        var order = await FindAsync(db, companyId, orderId, membership, ct);
        var courier = await CourierEndpoints.FindAsync(db, companyId, membership.UserId, ct);
        var now = clock.GetUtcNow();
        order.PickUp(membership.UserId, courier.SpeedKmh(), now);
        db.OrderEvents.Add(OrderEvent.Record(order, OrderEventKind.PickedUp, membership.UserId, now));
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await OrderQueries.DetailAsync(db, orderId, membership, ct));
    }

    private static async Task<Ok<OrderResponse>> Deliver(Guid companyId, Guid orderId, DeliverRequest request, ClaimsPrincipal user, KuryoDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Courier, ct);
        var order = await FindAsync(db, companyId, orderId, membership, ct);
        var now = clock.GetUtcNow();
        var attempts = order.CodeAttempts;
        try
        {
            order.Deliver(membership.UserId, request.Code, now);
        }
        catch (DomainException ex) when (ex.Code is ErrorCodes.WrongCode or ErrorCodes.CodeLocked && order.CodeAttempts > attempts)
        {
            // A wrong code counts even though the delivery fails.
            db.OrderEvents.Add(OrderEvent.Record(order, OrderEventKind.WrongCode, membership.UserId, now, $"Attempt {order.CodeAttempts} of {Order.MaxCodeAttempts}"));
            await db.SaveChangesAsync(ct);
            throw;
        }

        (await CourierEndpoints.FindAsync(db, companyId, membership.UserId, ct)).FinishOrder(now);
        db.OrderEvents.Add(OrderEvent.Record(order, OrderEventKind.Delivered, membership.UserId, now, "With the customer's code"));
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await OrderQueries.DetailAsync(db, orderId, membership, ct));
    }

    private static async Task<Ok<OrderResponse>> ConfirmDelivered(Guid companyId, Guid orderId, ReasonRequest request, ClaimsPrincipal user, KuryoDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var order = await FindAsync(db, companyId, orderId, membership, ct);
        var now = clock.GetUtcNow();
        order.ConfirmDelivered(now);
        if (order.CourierId is { } courierId)
        {
            (await CourierEndpoints.FindAsync(db, companyId, courierId, ct)).FinishOrder(now);
        }

        db.OrderEvents.Add(OrderEvent.Record(order, OrderEventKind.Delivered, membership.UserId, now, $"Confirmed by a dispatcher: {Text.Clean(request.Reason, 3, 300, "A reason")}"));
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await OrderQueries.DetailAsync(db, orderId, membership, ct));
    }

    private static async Task<Ok<OrderResponse>> Cancel(Guid companyId, Guid orderId, ReasonRequest request, ClaimsPrincipal user, KuryoDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Merchant, ct);
        var order = await FindAsync(db, companyId, orderId, membership, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (order.CourierId is { } carrier)
        {
            await LockCourierAsync(db, companyId, carrier, ct);
        }

        await LockOrderAsync(db, companyId, orderId, ct);
        await db.Entry(order).ReloadAsync(ct);
        if (!membership.IsDispatcher && order.Status == OrderStatus.PickedUp)
        {
            throw new DomainException(ErrorCodes.InvalidState, "The courier already has the order. Call the office to cancel it.");
        }

        var now = clock.GetUtcNow();
        var courierId = order.Cancel(request.Reason, now);
        if (courierId is { } id)
        {
            (await CourierEndpoints.FindAsync(db, companyId, id, ct)).FinishOrder(now);
        }

        await OfferEndpoints.WithdrawForOrderAsync(db, orderId, now, ct);
        db.OrderEvents.Add(OrderEvent.Record(order, OrderEventKind.Cancelled, membership.UserId, now, order.CancelReason));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.Ok(await OrderQueries.DetailAsync(db, orderId, membership, ct));
    }

    /// <summary>
    /// An order of the company that this member may see: dispatchers all, merchants their merchant's, couriers the
    /// ones they carry or carried. Anything else answers as not found.
    /// </summary>
    internal static async Task<Order> FindAsync(KuryoDbContext db, Guid companyId, Guid orderId, Membership membership, CancellationToken ct)
    {
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId, ct);
        var visible = order is not null && (membership.Serves(order.MerchantId) || (membership.Role == Role.Courier && order.CourierId == membership.UserId));
        return visible ? order! : throw new DomainException(ErrorCodes.NotFound, "There is no order with this id.");
    }

    internal static Task LockOrderAsync(KuryoDbContext db, Guid companyId, Guid orderId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT 1 FROM orders WHERE id = {orderId} AND company_id = {companyId} FOR UPDATE", ct);

    internal static Task LockCourierAsync(KuryoDbContext db, Guid companyId, Guid userId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT 1 FROM couriers WHERE company_id = {companyId} AND user_id = {userId} FOR UPDATE", ct);
}
