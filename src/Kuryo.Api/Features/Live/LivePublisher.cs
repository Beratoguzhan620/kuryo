using Kuryo.Api.Features.Couriers;
using Kuryo.Api.Features.Matching;
using Kuryo.Api.Features.Orders;
using Kuryo.Infrastructure.Live;
using Kuryo.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Kuryo.Api.Features.Live;

/// <summary>
/// Turns what a request committed into live messages. It reads the current state back rather than trusting what
/// the request meant to do, so every client ends up with what the database holds. A failure here is logged and
/// never fails the request: the data is already saved, and clients catch up on their next fetch.
/// </summary>
public sealed partial class LivePublisher(
    IHubContext<LiveHub, ILiveClient> live,
    IHubContext<TrackHub, ITrackClient> track,
    TimeProvider clock,
    ILogger<LivePublisher> logger)
{
    public async Task PublishAsync(KuryoDbContext db, Changes changes, CancellationToken ct)
    {
        if (changes.Orders.Count + changes.Couriers.Count + changes.Offers.Count == 0)
        {
            return;
        }

        try
        {
            await PublishCoreAsync(db, changes, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex);
        }
    }

    private async Task PublishCoreAsync(KuryoDbContext db, Changes changes, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var tracked = new HashSet<Guid>(changes.Orders);

        if (changes.Orders.Count > 0)
        {
            var orders = db.Orders.AsNoTracking().Where(o => changes.Orders.Contains(o.Id));
            var companies = await orders.Select(o => new { o.Id, o.CompanyId }).ToDictionaryAsync(o => o.Id, o => o.CompanyId, ct);
            foreach (var summary in await OrderQueries.SummariesAsync(db, orders, ct))
            {
                var companyId = companies[summary.Id];
                await live.Clients.Group(LiveGroups.Operations(companyId)).OrderChanged(summary);
                await live.Clients.Group(LiveGroups.Merchant(summary.MerchantId)).OrderChanged(summary);
                if (summary.CourierId is { } courierId)
                {
                    await live.Clients.Group(LiveGroups.Courier(companyId, courierId)).OrderChanged(summary);
                }

                // The courier who just lost the order hears about it too, so it leaves their screen.
                foreach (var former in changes.FormerCarriers.Where(f => f.CompanyId == companyId && f.UserId != summary.CourierId))
                {
                    await live.Clients.Group(LiveGroups.Courier(companyId, former.UserId)).OrderChanged(summary);
                }
            }
        }

        foreach (var company in changes.Couriers.GroupBy(c => c.CompanyId))
        {
            var ids = company.Select(c => c.UserId).ToList();
            foreach (var courier in await CourierEndpoints.QueryAsync(db, company.Key, ids, ct))
            {
                await live.Clients.Group(LiveGroups.Operations(company.Key)).CourierChanged(courier);
                await live.Clients.Group(LiveGroups.Courier(company.Key, courier.UserId)).CourierChanged(courier);
                if (courier.OrderId is { } carrying)
                {
                    // A courier on the way moves the customer's map.
                    tracked.Add(carrying);
                }
            }
        }

        foreach (var courier in changes.Offers)
        {
            var offer = await OfferEndpoints.CurrentAsync(db, courier.CompanyId, courier.UserId, now, ct);
            await live.Clients.Group(LiveGroups.Courier(courier.CompanyId, courier.UserId)).OfferChanged(offer);
        }

        if (tracked.Count > 0)
        {
            foreach (var (orderId, view) in await TrackQueries.ForOrdersAsync(db, tracked, now, ct))
            {
                await track.Clients.Group(LiveGroups.Track(orderId)).TrackChanged(view);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Live update could not be sent; clients will catch up on their next fetch")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

/// <summary>After a request succeeds, sends what it changed to the live feed.</summary>
internal sealed class LiveFeedMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ChangeFeed feed, LivePublisher publisher)
    {
        try
        {
            await next(context);
        }
        catch
        {
            feed.Discard();
            throw;
        }

        var changes = feed.Drain();
        if (context.Response.StatusCode < StatusCodes.Status400BadRequest)
        {
            await publisher.PublishAsync(context.RequestServices.GetRequiredService<KuryoDbContext>(), changes, CancellationToken.None);
        }
    }
}
