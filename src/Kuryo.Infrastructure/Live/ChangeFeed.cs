using Kuryo.Core.Couriers;
using Kuryo.Core.Offers;
using Kuryo.Core.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kuryo.Infrastructure.Live;

/// <summary>A courier inside one company.</summary>
public readonly record struct CourierKey(Guid CompanyId, Guid UserId);

/// <summary>
/// What one request (or one matching pass) changed: orders, couriers, and couriers whose open offer changed. Filled
/// as changes are saved and read once the work has committed, so the live feed only ever shows committed state.
/// </summary>
public sealed class ChangeFeed
{
    private readonly Lock _gate = new();
    private readonly HashSet<Guid> _orders = [];
    private readonly HashSet<CourierKey> _couriers = [];
    private readonly HashSet<CourierKey> _offers = [];
    private readonly HashSet<CourierKey> _formerCarriers = [];

    public bool IsEmpty
    {
        get
        {
            lock (_gate)
            {
                return _orders.Count + _couriers.Count + _offers.Count == 0;
            }
        }
    }

    public void OrderChanged(Guid orderId)
    {
        lock (_gate)
        {
            _orders.Add(orderId);
        }
    }

    public void CourierChanged(CourierKey courier)
    {
        lock (_gate)
        {
            _couriers.Add(courier);
        }
    }

    /// <summary>The courier's open offer appeared, was answered, ran out or was taken back.</summary>
    public void OfferChanged(CourierKey courier)
    {
        lock (_gate)
        {
            _offers.Add(courier);
        }
    }

    /// <summary>A courier who was carrying an order and no longer is, so their app can drop it.</summary>
    public void CarrierDropped(CourierKey courier)
    {
        lock (_gate)
        {
            _formerCarriers.Add(courier);
        }
    }

    /// <summary>Takes everything collected so far and starts empty again.</summary>
    public Changes Drain()
    {
        lock (_gate)
        {
            var changes = new Changes([.. _orders], [.. _couriers], [.. _offers], [.. _formerCarriers]);
            _orders.Clear();
            _couriers.Clear();
            _offers.Clear();
            _formerCarriers.Clear();
            return changes;
        }
    }

    public void Discard() => Drain();
}

public sealed record Changes(
    IReadOnlyList<Guid> Orders,
    IReadOnlyList<CourierKey> Couriers,
    IReadOnlyList<CourierKey> Offers,
    IReadOnlyList<CourierKey> FormerCarriers);

/// <summary>Notes every order, courier and offer a save touches.</summary>
internal sealed class ChangeFeedInterceptor(ChangeFeed feed) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Collect(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            switch (entry.Entity)
            {
                case Order order:
                    feed.OrderChanged(order.Id);
                    if (entry.State == EntityState.Modified
                        && entry.OriginalValues[nameof(Order.CourierId)] is Guid before
                        && before != order.CourierId)
                    {
                        feed.CarrierDropped(new CourierKey(order.CompanyId, before));
                    }

                    break;
                case Courier courier:
                    feed.CourierChanged(new CourierKey(courier.CompanyId, courier.UserId));
                    break;
                case Offer offer:
                    feed.OfferChanged(new CourierKey(offer.CompanyId, offer.CourierId));
                    feed.OrderChanged(offer.OrderId);
                    break;
            }
        }
    }
}
