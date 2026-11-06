namespace Kuryo.Core.Offers;

public enum OfferStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    Expired = 3,

    /// <summary>Taken back: the order was cancelled or assigned by hand, or the courier went offline.</summary>
    Withdrawn = 4,
}

/// <summary>An order offered to one courier for a limited time. At most one offer is pending per order and per courier.</summary>
public sealed class Offer
{
    private Offer()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid CourierId { get; private set; }

    public OfferStatus Status { get; private set; }

    public DateTimeOffset OfferedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RespondedAt { get; private set; }

    /// <summary>How far the courier was from the pickup when the offer went out.</summary>
    public double DistanceKm { get; private set; }

    public static Offer Make(Guid companyId, Guid orderId, Guid courierId, double distanceKm, int seconds, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            CompanyId = companyId,
            OrderId = orderId,
            CourierId = courierId,
            Status = OfferStatus.Pending,
            OfferedAt = now,
            ExpiresAt = now.AddSeconds(seconds),
            DistanceKm = Math.Round(distanceKm, 2),
        };

    public bool IsLive(DateTimeOffset now) => Status == OfferStatus.Pending && now < ExpiresAt;

    public void Accept(DateTimeOffset now) => Respond(OfferStatus.Accepted, now, "accept");

    public void Decline(DateTimeOffset now) => Respond(OfferStatus.Declined, now, "decline");

    public void Withdraw(DateTimeOffset now)
    {
        if (Status == OfferStatus.Pending)
        {
            Status = OfferStatus.Withdrawn;
            RespondedAt = now;
        }
    }

    private void Respond(OfferStatus status, DateTimeOffset now, string verb)
    {
        if (!IsLive(now))
        {
            throw new DomainException(ErrorCodes.OfferGone, Status == OfferStatus.Pending || Status == OfferStatus.Expired
                ? $"This offer ran out; it is too late to {verb} it."
                : $"This offer was already {Status.ToString().ToLowerInvariant()}.");
        }

        Status = status;
        RespondedAt = now;
    }
}
