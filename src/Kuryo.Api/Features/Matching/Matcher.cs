using Kuryo.Core.Companies;
using Kuryo.Core.Couriers;
using Kuryo.Core.Geo;
using Kuryo.Core.Offers;
using Kuryo.Core.Orders;
using Kuryo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Kuryo.Api.Features.Matching;

public sealed record MatchingPass(int Expired, int Offered, int GaveUp);

/// <summary>
/// Sends each searching order to the nearest free courier, one timed offer at a time. When an offer is declined or
/// runs out, the next nearest courier gets it; after the company's number of offers a dispatcher takes over.
/// Safe to run on several instances at once: orders and couriers are claimed with <c>SKIP LOCKED</c>, and the
/// database allows one pending offer per order and per courier.
/// </summary>
public sealed partial class Matcher(IServiceScopeFactory scopes, TimeProvider clock, IOptions<MatchingOptions> options, ILogger<Matcher> logger)
{
    public async Task<MatchingPass> RunOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KuryoDbContext>();
        var expired = await ExpireAsync(db, ct);
        var (offered, gaveUp) = await DispatchAsync(db, ct);
        if (expired + offered + gaveUp > 0)
        {
            LogPass(logger, expired, offered, gaveUp);
        }

        return new MatchingPass(expired, offered, gaveUp);
    }

    /// <summary>Ends every offer whose time is up, in one statement, and notes it on each order.</summary>
    private async Task<int> ExpireAsync(KuryoDbContext db, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var ids = await db.Database.SqlQuery<Guid>($"""
            UPDATE offers SET status = 'Expired', responded_at = {now}
            WHERE status = 'Pending' AND expires_at <= {now}
            RETURNING id AS "Value"
            """).ToListAsync(ct);
        if (ids.Count == 0)
        {
            return 0;
        }

        var rows = await (
            from f in db.Offers.AsNoTracking()
            join o in db.Orders.AsNoTracking() on f.OrderId equals o.Id
            join u in db.Users on f.CourierId equals u.Id
            where ids.Contains(f.Id)
            select new { Order = o, u.DisplayName }).ToListAsync(ct);
        foreach (var row in rows)
        {
            db.OrderEvents.Add(OrderEvent.Record(row.Order, OrderEventKind.OfferExpired, null, now, $"{row.DisplayName} did not answer"));
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ids.Count;
    }

    private async Task<(int Offered, int GaveUp)> DispatchAsync(KuryoDbContext db, CancellationToken ct)
    {
        var offered = 0;
        var gaveUp = 0;
        var seen = new List<Guid>();
        while (seen.Count < options.Value.BatchSize)
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var skip = seen.ToArray();
            // The oldest searching order nobody is looking at yet; another instance working on one is skipped, not waited for.
            var next = await db.Database.SqlQuery<Guid>($"""
                SELECT o.id AS "Value" FROM orders o
                WHERE o.status = 'Searching' AND o.id <> ALL({skip})
                  AND NOT EXISTS (SELECT 1 FROM offers f WHERE f.order_id = o.id AND f.status = 'Pending')
                ORDER BY o.search_started_at
                LIMIT 1
                FOR UPDATE OF o SKIP LOCKED
                """).ToListAsync(ct);
            if (next.Count == 0)
            {
                break;
            }

            var orderId = next[0];
            seen.Add(orderId);
            // The lock is fresh but the search above read an older snapshot: another instance may have just made an offer.
            if (await db.Offers.AnyAsync(f => f.OrderId == orderId && f.Status == OfferStatus.Pending, ct))
            {
                await transaction.RollbackAsync(ct);
                continue;
            }

            var order = await db.Orders.SingleAsync(o => o.Id == orderId, ct);
            var company = await db.Companies.AsNoTracking().SingleAsync(c => c.Id == order.CompanyId, ct);
            var now = clock.GetUtcNow();
            if (order.SearchIsOver(company.MaxOffers, company.OfferSeconds, now))
            {
                order.GiveUp(now);
                db.OrderEvents.Add(OrderEvent.Record(order, OrderEventKind.Unassigned, null, now,
                    order.OffersMade == 0 ? "No courier was free nearby" : $"No courier took it after {order.OffersMade} {(order.OffersMade == 1 ? "offer" : "offers")}"));
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                gaveUp++;
                continue;
            }

            var courier = await NearestAsync(db, order, company, now, ct);
            if (courier is null)
            {
                await transaction.RollbackAsync(ct);
                continue;
            }

            var km = GeoPoint.DistanceKm(courier.Location!, order.PickupLocation);
            order.Offered(now);
            db.Offers.Add(Offer.Make(order.CompanyId, order.Id, courier.UserId, km, company.OfferSeconds, now));
            var name = await db.Users.Where(u => u.Id == courier.UserId).Select(u => u.DisplayName).SingleAsync(ct);
            db.OrderEvents.Add(OrderEvent.Record(order, OrderEventKind.Offered, null, now, $"{name}, {km:0.0} km from the pickup, {company.OfferSeconds} s to answer"));
            try
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                offered++;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Another instance offered this courier something in the same instant; this order tries again next pass.
                await transaction.RollbackAsync(ct);
            }
        }

        return (offered, gaveUp);
    }

    /// <summary>
    /// The nearest courier who is free, was seen recently within the company's search radius, has no other offer
    /// open and has not already turned this order down. The GiST index answers the nearest-first ordering.
    /// </summary>
    private static async Task<Courier?> NearestAsync(KuryoDbContext db, Order order, Company company, DateTimeOffset now, CancellationToken ct)
    {
        var freshSince = now - Courier.FreshFor;
        var radius = company.SearchRadiusKm * 1000;
        var ids = await db.Database.SqlQuery<Guid>($"""
            SELECT c.user_id AS "Value" FROM couriers c, orders o
            WHERE o.id = {order.Id} AND c.company_id = o.company_id
              AND c.status = 'Available' AND c.location IS NOT NULL AND c.location_at >= {freshSince}
              AND ST_DWithin(c.location, o.pickup_location, {radius})
              AND NOT EXISTS (SELECT 1 FROM offers f WHERE f.courier_id = c.user_id AND (f.status = 'Pending' OR f.order_id = o.id))
            ORDER BY c.location <-> o.pickup_location
            LIMIT 1
            FOR UPDATE OF c SKIP LOCKED
            """).ToListAsync(ct);
        if (ids.Count == 0 || await db.Offers.AnyAsync(f => f.CourierId == ids[0] && f.Status == OfferStatus.Pending, ct))
        {
            return null;
        }

        return await db.Couriers.SingleAsync(c => c.CompanyId == company.Id && c.UserId == ids[0], ct);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Matching pass: {Expired} offers expired, {Offered} sent, {GaveUp} orders handed to dispatchers")]
    private static partial void LogPass(ILogger logger, int expired, int offered, int gaveUp);
}

/// <summary>Runs the matcher in a loop, waking early when an endpoint pokes it.</summary>
public sealed partial class MatchingWorker(Matcher matcher, MatchingSignal signal, IOptions<MatchingOptions> options, ILogger<MatchingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await matcher.RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }

            await signal.WaitAsync(options.Value.Interval, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Matching pass failed; trying again shortly")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
