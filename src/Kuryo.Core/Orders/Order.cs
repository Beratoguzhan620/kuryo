using System.Security.Cryptography;
using Kuryo.Core.Geo;
using Kuryo.Core.Merchants;
using Kuryo.Core.Zones;
using NetTopologySuite.Geometries;

namespace Kuryo.Core.Orders;

/// <summary>
/// Where an order stands. Searching → Assigned → PickedUp → Delivered is the usual path. An order that finds no
/// courier waits as Unassigned for a dispatcher; one whose courier hands it back searches again; anything not yet
/// delivered can be cancelled.
/// </summary>
public enum OrderStatus
{
    Searching = 0,
    Unassigned = 1,
    Assigned = 2,
    PickedUp = 3,
    Delivered = 4,
    Cancelled = 5,
}

/// <summary>A delivery from a merchant to a customer.</summary>
public sealed class Order
{
    public const int NameMaxLength = 80;
    public const int AddressMaxLength = 200;
    public const int NotesMaxLength = 500;
    public const int ItemsMaxLength = 1000;
    public const int MaxCodeAttempts = 5;

    private Order()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public int Number { get; private set; }

    public string Code => Format(Number);

    public Guid MerchantId { get; private set; }

    public Point PickupLocation { get; private set; } = null!;

    public string PickupAddress { get; private set; } = string.Empty;

    public string CustomerName { get; private set; } = string.Empty;

    public string CustomerPhone { get; private set; } = string.Empty;

    public string DropoffAddress { get; private set; } = string.Empty;

    public Point DropoffLocation { get; private set; } = null!;

    /// <summary>Floor, door, "ring twice".</summary>
    public string DropoffNotes { get; private set; } = string.Empty;

    public string Items { get; private set; } = string.Empty;

    public Guid ZoneId { get; private set; }

    public long DeliveryFeeMinor { get; private set; }

    /// <summary>Straight line from pickup to drop-off.</summary>
    public double DistanceKm { get; private set; }

    public OrderStatus Status { get; private set; }

    /// <summary>When the merchant expects the order to be ready for pickup.</summary>
    public DateTimeOffset ReadyAt { get; private set; }

    public Guid? CourierId { get; private set; }

    /// <summary>How many couriers this order has been offered to; a dispatcher steps in after the company's limit.</summary>
    public int OffersMade { get; private set; }

    public DateTimeOffset? AssignedAt { get; private set; }

    public DateTimeOffset? PickedUpAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? EstimatedDeliveryAt { get; private set; }

    public string CancelReason { get; private set; } = string.Empty;

    /// <summary>Opens the customer's tracking page; long and random, so it cannot be guessed.</summary>
    public string TrackingToken { get; private set; } = string.Empty;

    /// <summary>The customer shows it to the courier, who enters it to finish the delivery.</summary>
    public string DeliveryCode { get; private set; } = string.Empty;

    public int CodeAttempts { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsOpen => Status is not (OrderStatus.Delivered or OrderStatus.Cancelled);

    public bool CodeLocked => CodeAttempts >= MaxCodeAttempts;

    public static string Format(int number) => $"KR-{number:D6}";

    public static Order Create(
        Guid companyId,
        int number,
        Merchant merchant,
        Zone zone,
        string customerName,
        string customerPhone,
        string dropoffAddress,
        double latitude,
        double longitude,
        string? dropoffNotes,
        string? items,
        Guid createdBy,
        DateTimeOffset now)
    {
        if (!merchant.Active)
        {
            throw new DomainException(ErrorCodes.InvalidState, "This merchant is paused and cannot send orders.");
        }

        var dropoff = GeoPoint.At(latitude, longitude);
        if (!zone.Active || !zone.Contains(dropoff))
        {
            throw new DomainException(ErrorCodes.OutsideZone, "The address is outside the areas the company delivers to.");
        }

        return new Order
        {
            Id = Guid.CreateVersion7(now),
            CompanyId = companyId,
            Number = number > 0 ? number : throw new ArgumentOutOfRangeException(nameof(number)),
            MerchantId = merchant.Id,
            PickupLocation = merchant.Location,
            PickupAddress = merchant.Address,
            CustomerName = Text.Clean(customerName, 2, NameMaxLength, "The customer's name"),
            CustomerPhone = Text.Clean(customerPhone, 7, 30, "The customer's phone"),
            DropoffAddress = Text.Clean(dropoffAddress, 3, AddressMaxLength, "The delivery address"),
            DropoffLocation = dropoff,
            DropoffNotes = Text.CleanParagraphs(dropoffNotes, NotesMaxLength, "Delivery notes"),
            Items = Text.CleanParagraphs(items, ItemsMaxLength, "The items"),
            ZoneId = zone.Id,
            DeliveryFeeMinor = zone.DeliveryFeeMinor,
            DistanceKm = Math.Round(GeoPoint.DistanceKm(merchant.Location, dropoff), 2),
            Status = OrderStatus.Searching,
            ReadyAt = now.AddMinutes(merchant.PrepMinutes),
            TrackingToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_'),
            DeliveryCode = RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4"),
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>An offer went out for this order.</summary>
    public void Offered(DateTimeOffset now)
    {
        Require("be offered", OrderStatus.Searching);
        OffersMade++;
        UpdatedAt = now;
    }

    /// <summary>A courier takes the order, by accepting an offer or because a dispatcher said so.</summary>
    public void Assign(Guid courierId, double courierSpeedKmh, double courierToPickupKm, DateTimeOffset now)
    {
        Require("be assigned", OrderStatus.Searching, OrderStatus.Unassigned);
        CourierId = courierId;
        AssignedAt = now;
        Status = OrderStatus.Assigned;
        Estimate(courierSpeedKmh, courierToPickupKm, now);
        UpdatedAt = now;
    }

    /// <summary>Nobody took it: a dispatcher has to.</summary>
    public void GiveUp(DateTimeOffset now)
    {
        Require("wait for a dispatcher", OrderStatus.Searching);
        Status = OrderStatus.Unassigned;
        UpdatedAt = now;
    }

    /// <summary>Starts looking for a courier again, with a fresh count of offers.</summary>
    public void SearchAgain(DateTimeOffset now)
    {
        Require("search again", OrderStatus.Unassigned);
        Status = OrderStatus.Searching;
        OffersMade = 0;
        UpdatedAt = now;
    }

    /// <summary>The courier cannot carry it after all (or a dispatcher takes it away): back to searching.</summary>
    public Guid Release(DateTimeOffset now)
    {
        Require("be handed back", OrderStatus.Assigned);
        var courier = CourierId!.Value;
        CourierId = null;
        AssignedAt = null;
        EstimatedDeliveryAt = null;
        Status = OrderStatus.Searching;
        OffersMade = 0;
        UpdatedAt = now;
        return courier;
    }

    public void PickUp(Guid courierId, double courierSpeedKmh, DateTimeOffset now)
    {
        EnsureCourier(courierId);
        Require("be picked up", OrderStatus.Assigned);
        PickedUpAt = now;
        Status = OrderStatus.PickedUp;
        EstimatedDeliveryAt = now + GeoPoint.RideTime(DistanceKm, courierSpeedKmh);
        UpdatedAt = now;
    }

    /// <summary>
    /// Finishes the delivery with the customer's code. Wrong codes count; after five, only a dispatcher can close it,
    /// so a code cannot be guessed by trying.
    /// </summary>
    public void Deliver(Guid courierId, string code, DateTimeOffset now)
    {
        EnsureCourier(courierId);
        Require("be delivered", OrderStatus.PickedUp);
        if (CodeLocked)
        {
            throw new DomainException(ErrorCodes.CodeLocked, "Too many wrong codes. Call the office to close this delivery.");
        }

        if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes((code ?? string.Empty).Trim()), System.Text.Encoding.ASCII.GetBytes(DeliveryCode)))
        {
            CodeAttempts++;
            UpdatedAt = now;
            var left = MaxCodeAttempts - CodeAttempts;
            throw new DomainException(left > 0 ? ErrorCodes.WrongCode : ErrorCodes.CodeLocked,
                left > 0 ? $"That is not the code. {left} {(left == 1 ? "try" : "tries")} left." : "Too many wrong codes. Call the office to close this delivery.");
        }

        Complete(now);
    }

    /// <summary>A dispatcher closes the delivery without the code, for example after talking to the customer.</summary>
    public void ConfirmDelivered(DateTimeOffset now)
    {
        Require("be delivered", OrderStatus.PickedUp);
        Complete(now);
    }

    /// <summary>Returns the courier who was carrying it, if any, so they can be freed.</summary>
    public Guid? Cancel(string reason, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            throw new DomainException(ErrorCodes.InvalidState, $"This order is {Status.ToString().ToLowerInvariant()} and can no longer be cancelled.");
        }

        CancelReason = Text.Clean(reason, 3, NotesMaxLength, "A reason");
        Status = OrderStatus.Cancelled;
        UpdatedAt = now;
        return CourierId;
    }

    /// <summary>Estimates delivery: the courier reaches the pickup (or waits for the order), then rides to the customer.</summary>
    public void Estimate(double courierSpeedKmh, double courierToPickupKm, DateTimeOffset now)
    {
        var atPickup = now + GeoPoint.RideTime(courierToPickupKm, courierSpeedKmh);
        var leaves = atPickup > ReadyAt ? atPickup : ReadyAt;
        EstimatedDeliveryAt = leaves + GeoPoint.RideTime(DistanceKm, courierSpeedKmh);
    }

    private void Complete(DateTimeOffset now)
    {
        DeliveredAt = now;
        Status = OrderStatus.Delivered;
        UpdatedAt = now;
    }

    private void EnsureCourier(Guid courierId)
    {
        if (CourierId != courierId)
        {
            throw new DomainException(ErrorCodes.NotYourOrder, "This order is assigned to someone else.");
        }
    }

    private void Require(string action, params OrderStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new DomainException(ErrorCodes.InvalidState, $"An order that is {Describe(Status)} cannot {action}.");
        }
    }

    private static string Describe(OrderStatus status) => status switch
    {
        OrderStatus.PickedUp => "picked up",
        _ => status.ToString().ToLowerInvariant(),
    };
}

public enum OrderEventKind
{
    Created = 0,
    Offered = 1,
    OfferDeclined = 2,
    OfferExpired = 3,
    Assigned = 4,
    Unassigned = 5,
    Released = 6,
    PickedUp = 7,
    Delivered = 8,
    Cancelled = 9,
    WrongCode = 10,
    SearchAgain = 11,
}

/// <summary>One line of an order's history.</summary>
public sealed class OrderEvent
{
    public const int DetailMaxLength = 500;

    private OrderEvent()
    {
    }

    public long Id { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid CompanyId { get; private set; }

    public OrderEventKind Kind { get; private set; }

    /// <summary>Who did it; empty when the system did (offers, expiries).</summary>
    public Guid? ActorId { get; private set; }

    public DateTimeOffset At { get; private set; }

    public string Detail { get; private set; } = string.Empty;

    public static OrderEvent Record(Order order, OrderEventKind kind, Guid? actorId, DateTimeOffset at, string? detail = null) =>
        new()
        {
            OrderId = order.Id,
            CompanyId = order.CompanyId,
            Kind = kind,
            ActorId = actorId,
            At = at,
            Detail = detail is null ? string.Empty : detail.Length <= DetailMaxLength ? detail : detail[..DetailMaxLength],
        };
}
