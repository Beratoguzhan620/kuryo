using Kuryo.Core.Couriers;
using Kuryo.Core.Orders;

namespace Kuryo.Api.Features.Orders;

public sealed record CreateOrderRequest(
    Guid MerchantId,
    string CustomerName,
    string CustomerPhone,
    string DropoffAddress,
    double Latitude,
    double Longitude,
    string? DropoffNotes,
    string? Items);

public sealed record AssignRequest(Guid CourierId);

public sealed record DeliverRequest(string Code);

public sealed record ReasonRequest(string Reason);

public sealed record OrderSummary(
    Guid Id,
    string Code,
    OrderStatus Status,
    Guid MerchantId,
    string MerchantName,
    string CustomerName,
    string DropoffAddress,
    double Latitude,
    double Longitude,
    Guid? CourierId,
    string? CourierName,
    int OffersMade,
    DateTimeOffset ReadyAt,
    DateTimeOffset? EstimatedDeliveryAt,
    DateTimeOffset CreatedAt);

public sealed record PlaceRef(string Address, double Latitude, double Longitude);

public sealed record MerchantRef(Guid Id, string Name, string Phone);

public sealed record CourierRef(Guid Id, string Name, Vehicle Vehicle, string Plate, double? Latitude, double? Longitude, DateTimeOffset? LocationAt);

public sealed record OrderEventResponse(long Id, OrderEventKind Kind, string? ActorName, DateTimeOffset At, string Detail);

/// <summary>
/// One order. <c>deliveryCode</c> and <c>trackingToken</c> are only shown to the merchant and dispatchers: the
/// customer gets them, the courier must ask for the code at the door.
/// </summary>
public sealed record OrderResponse(
    Guid Id,
    int Number,
    string Code,
    OrderStatus Status,
    MerchantRef Merchant,
    PlaceRef Pickup,
    string CustomerName,
    string CustomerPhone,
    PlaceRef Dropoff,
    string DropoffNotes,
    string Items,
    string ZoneName,
    decimal DeliveryFee,
    double DistanceKm,
    DateTimeOffset ReadyAt,
    CourierRef? Courier,
    int OffersMade,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? PickedUpAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? EstimatedDeliveryAt,
    string CancelReason,
    string? TrackingToken,
    string? DeliveryCode,
    int CodeAttempts,
    IReadOnlyList<OrderEventResponse> History,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
