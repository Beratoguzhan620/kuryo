namespace Kuryo.IntegrationTests.Support;

public sealed record UserDto(Guid Id, string Email, string DisplayName);

public sealed record SessionDto(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

public sealed record CompanyDto(
    Guid Id, string Name, string Role, Guid? MerchantId, string TimeZone, int OfferSeconds, double SearchRadiusKm, int MaxOffers,
    int Members, int Merchants, int CouriersOnline, int OpenOrders, DateTimeOffset CreatedAt);

public sealed record MeDto(UserDto User, List<CompanyDto> Companies);

public sealed record MemberDto(Guid UserId, string DisplayName, string Email, string Role, Guid? MerchantId, string? MerchantName, DateTimeOffset JoinedAt);

public sealed record PageDto<T>(List<T> Items, int Total, int PageNumber, int PageSize);

public sealed record MerchantDto(Guid Id, string Name, string Phone, string Address, double Latitude, double Longitude, int PrepMinutes, bool Active, int OpenOrders, DateTimeOffset CreatedAt);

public sealed record ZoneDto(Guid Id, string Name, List<double[]> Corners, decimal DeliveryFee, bool Active, double AreaKm2);

public sealed record ZoneCheckDto(bool Delivers, Guid? ZoneId, string? ZoneName, decimal? DeliveryFee);

public sealed record CourierDto(
    Guid UserId, string DisplayName, string Vehicle, string Plate, string Status, DateTimeOffset StatusSince, double? Latitude, double? Longitude,
    DateTimeOffset? LocationAt, Guid? OrderId, string? OrderCode);

public sealed record OrderSummaryDto(
    Guid Id, string Code, string Status, Guid MerchantId, string MerchantName, string CustomerName, string DropoffAddress, double Latitude, double Longitude,
    Guid? CourierId, string? CourierName, int OffersMade, DateTimeOffset ReadyAt, DateTimeOffset? EstimatedDeliveryAt, DateTimeOffset CreatedAt);

public sealed record PlaceDto(string Address, double Latitude, double Longitude);

public sealed record MerchantRefDto(Guid Id, string Name, string Phone);

public sealed record CourierRefDto(Guid Id, string Name, string Vehicle, string Plate, double? Latitude, double? Longitude, DateTimeOffset? LocationAt);

public sealed record HistoryDto(long Id, string Kind, string? ActorName, DateTimeOffset At, string Detail);

public sealed record OrderDto(
    Guid Id, int Number, string Code, string Status, MerchantRefDto Merchant, PlaceDto Pickup, string CustomerName, string CustomerPhone, PlaceDto Dropoff,
    string DropoffNotes, string Items, string ZoneName, decimal DeliveryFee, double DistanceKm, DateTimeOffset ReadyAt, CourierRefDto? Courier, int OffersMade,
    DateTimeOffset? AssignedAt, DateTimeOffset? PickedUpAt, DateTimeOffset? DeliveredAt, DateTimeOffset? EstimatedDeliveryAt, string CancelReason,
    string? TrackingToken, string? DeliveryCode, int CodeAttempts, List<HistoryDto> History, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
