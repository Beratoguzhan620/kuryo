using Kuryo.Api.Features.Companies;
using Kuryo.Api.Features.Couriers;
using Kuryo.Api.Features.Matching;
using Kuryo.Api.Features.Orders;
using Kuryo.Core;
using Kuryo.Core.Companies;
using Kuryo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Kuryo.Api.Features.Live;

/// <summary>Messages the live hub sends. Every one carries the full current state, so a client never has to merge deltas.</summary>
public interface ILiveClient
{
    /// <summary>An order was created or moved on. Dispatchers get all, merchants theirs, couriers the ones they carry.</summary>
    Task OrderChanged(OrderSummary order);

    /// <summary>A courier's status or position changed (dispatchers, and the courier's own devices).</summary>
    Task CourierChanged(CourierResponse courier);

    /// <summary>The courier's open offer: a new one, or null when it was answered, ran out or was taken back.</summary>
    Task OfferChanged(OfferResponse? offer);
}

public interface ITrackClient
{
    Task TrackChanged(TrackView view);
}

public sealed record WatchResponse(Guid CompanyId, Role Role, Guid? MerchantId);

internal static class LiveGroups
{
    public static string Operations(Guid companyId) => $"ops:{companyId}";

    public static string Merchant(Guid merchantId) => $"merchant:{merchantId}";

    public static string Courier(Guid companyId, Guid userId) => $"courier:{companyId}:{userId}";

    public static string Track(Guid orderId) => $"track:{orderId}";
}

/// <summary>
/// The live feed for signed-in members. A client calls <c>Watch(companyId)</c> once connected and from then on hears
/// what its role may see. Membership is checked when watching; a member removed later drops off at the next reconnect.
/// </summary>
[Authorize]
public sealed class LiveHub(KuryoDbContext db) : Hub<ILiveClient>
{
    public const string Path = "/hubs/live";

    public async Task<WatchResponse> Watch(Guid companyId)
    {
        Membership membership;
        try
        {
            membership = await CompanyAccess.RequireMemberAsync(db, companyId, Context.User!, Context.ConnectionAborted);
        }
        catch (DomainException ex)
        {
            throw new HubException(ex.Message);
        }

        var group = membership switch
        {
            { IsDispatcher: true } => LiveGroups.Operations(companyId),
            { Role: Role.Merchant, MerchantId: { } merchantId } => LiveGroups.Merchant(merchantId),
            _ => LiveGroups.Courier(companyId, membership.UserId),
        };
        await Groups.AddToGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
        return new WatchResponse(companyId, membership.Role, membership.MerchantId);
    }
}

/// <summary>The customer's live view of one delivery, opened with the tracking token alone.</summary>
[AllowAnonymous]
public sealed class TrackHub(KuryoDbContext db, TimeProvider clock) : Hub<ITrackClient>
{
    public const string Path = "/hubs/track";

    public async Task<TrackView> Follow(string token)
    {
        var found = await TrackQueries.ByTokenAsync(db, token, clock.GetUtcNow(), Context.ConnectionAborted)
            ?? throw new HubException("This tracking link is not valid or has ended.");
        await Groups.AddToGroupAsync(Context.ConnectionId, LiveGroups.Track(found.OrderId), Context.ConnectionAborted);
        return found.View;
    }
}
