namespace Kuryo.Core.Companies;

/// <summary>What a member does. Couriers and merchants each see their own corner; dispatchers run the operation; owners also manage the team.</summary>
public enum Role
{
    /// <summary>Rides: goes online, takes offers, picks up and delivers.</summary>
    Courier = 0,

    /// <summary>Works at one merchant: sends orders and follows them.</summary>
    Merchant = 1,

    /// <summary>Runs the operation: merchants, zones, couriers, every order.</summary>
    Dispatcher = 2,

    /// <summary>Also manages the team and the settings.</summary>
    Owner = 3,
}

/// <summary>A courier company: its riders, the merchants it delivers for, its zones and orders.</summary>
public sealed class Company
{
    public const int NameMinLength = 2;
    public const int NameMaxLength = 80;
    public const string DefaultTimeZone = "Europe/Istanbul";

    private Company()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string TimeZone { get; private set; } = DefaultTimeZone;

    /// <summary>The number the next order gets; claimed with a single UPDATE … RETURNING.</summary>
    public int NextOrderNumber { get; private set; } = 1001;

    /// <summary>How long a courier has to accept an offer.</summary>
    public int OfferSeconds { get; private set; } = 30;

    /// <summary>How far from the pickup a courier may be to get an offer.</summary>
    public double SearchRadiusKm { get; private set; } = 6;

    /// <summary>After this many offers that came to nothing, the order waits for a dispatcher.</summary>
    public int MaxOffers { get; private set; } = 5;

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static (Company Company, Membership Owner) Create(string name, Guid ownerId, DateTimeOffset now)
    {
        var company = new Company { Id = Guid.CreateVersion7(now), Name = Text.Clean(name, NameMinLength, NameMaxLength, "A company name"), CreatedBy = ownerId, CreatedAt = now };
        return (company, Membership.Create(company.Id, ownerId, Role.Owner, null, now));
    }

    public void Rename(string name) => Name = Text.Clean(name, NameMinLength, NameMaxLength, "A company name");

    public void Configure(int offerSeconds, double searchRadiusKm, int maxOffers)
    {
        if (offerSeconds is < 10 or > 300)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "An offer lasts 10 to 300 seconds.");
        }

        if (searchRadiusKm is < 0.5 or > 50 || !double.IsFinite(searchRadiusKm))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "The search radius is 0.5 to 50 km.");
        }

        if (maxOffers is < 1 or > 20)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "An order gets 1 to 20 offers before a dispatcher steps in.");
        }

        OfferSeconds = offerSeconds;
        SearchRadiusKm = searchRadiusKm;
        MaxOffers = maxOffers;
    }

    /// <summary>
    /// Checks that a change to the team leaves somebody who can manage it.
    /// <paramref name="ownersAfter"/> is how many owners there would be after the change.
    /// </summary>
    public static void EnsureAnOwnerRemains(int ownersAfter)
    {
        if (ownersAfter < 1)
        {
            throw new DomainException(ErrorCodes.InvalidState, "A company needs at least one owner. Make someone else an owner first.");
        }
    }
}

public sealed class Membership
{
    private Membership()
    {
    }

    public Guid CompanyId { get; private set; }

    public Guid UserId { get; private set; }

    public Role Role { get; private set; }

    /// <summary>The merchant a merchant member works for; empty for everyone else.</summary>
    public Guid? MerchantId { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    public static Membership Create(Guid companyId, Guid userId, Role role, Guid? merchantId, DateTimeOffset now)
    {
        var membership = new Membership { CompanyId = companyId, UserId = userId, JoinedAt = now };
        membership.ChangeRole(role, merchantId);
        return membership;
    }

    /// <summary>Dispatchers and owners run the operation.</summary>
    public bool IsDispatcher => Role >= Role.Dispatcher;

    public bool Can(Role needed) => needed switch
    {
        Role.Courier or Role.Merchant => Role == needed || IsDispatcher,
        _ => Role >= needed,
    };

    /// <summary>Whether this member may see and act for a merchant's orders.</summary>
    public bool Serves(Guid merchantId) => IsDispatcher || (Role == Role.Merchant && MerchantId == merchantId);

    public void ChangeRole(Role role, Guid? merchantId)
    {
        if (!Enum.IsDefined(role))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A role is Courier, Merchant, Dispatcher or Owner.");
        }

        if ((role == Role.Merchant) != (merchantId is not null))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, role == Role.Merchant ? "A merchant member needs the merchant they work for." : "Only merchant members belong to a merchant.");
        }

        Role = role;
        MerchantId = merchantId;
    }
}
