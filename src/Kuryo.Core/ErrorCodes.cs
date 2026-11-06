namespace Kuryo.Core;

/// <summary>The stable error codes returned in problem responses.</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string NotFound = "not_found";
    public const string Forbidden = "forbidden";
    public const string InvalidState = "invalid_state";
    public const string Conflict = "conflict";
    public const string EmailTaken = "email_taken";
    public const string InvalidCredentials = "invalid_credentials";
    public const string RateLimited = "rate_limited";
    public const string OutsideZone = "outside_zone";
    public const string CourierUnavailable = "courier_unavailable";
    public const string NotYourOrder = "not_your_order";
    public const string WrongCode = "wrong_code";
    public const string CodeLocked = "code_locked";
    public const string OfferGone = "offer_gone";
}
