using Kuryo.Core;
using Kuryo.Core.Companies;

namespace Kuryo.UnitTests;

public sealed class TeamTests
{
    private static Membership As(Role role, Guid? merchant = null) => Membership.Create(Guid.NewGuid(), Guid.NewGuid(), role, merchant, Builders.Now);

    [Theory]
    [InlineData(Role.Courier, Role.Courier, true)]
    [InlineData(Role.Courier, Role.Merchant, false)]
    [InlineData(Role.Courier, Role.Dispatcher, false)]
    [InlineData(Role.Dispatcher, Role.Courier, true)]
    [InlineData(Role.Dispatcher, Role.Merchant, true)]
    [InlineData(Role.Dispatcher, Role.Owner, false)]
    [InlineData(Role.Owner, Role.Dispatcher, true)]
    public void Couriers_and_merchants_have_their_own_corners_and_dispatchers_cover_both(Role role, Role needed, bool allowed) =>
        Assert.Equal(allowed, As(role, role == Role.Merchant ? Guid.NewGuid() : null).Can(needed));

    [Fact]
    public void A_merchant_member_serves_only_their_merchant()
    {
        var merchant = Guid.NewGuid();
        var member = As(Role.Merchant, merchant);

        Assert.True(member.Serves(merchant));
        Assert.False(member.Serves(Guid.NewGuid()));
        Assert.True(As(Role.Dispatcher).Serves(merchant));
        Assert.False(As(Role.Courier).Serves(merchant));
    }

    [Fact]
    public void Merchant_members_need_a_merchant_and_nobody_else_has_one()
    {
        Assert.Throws<DomainException>(() => As(Role.Merchant));
        Assert.Throws<DomainException>(() => As(Role.Courier, Guid.NewGuid()));
    }

    [Fact]
    public void A_company_starts_with_its_creator_as_owner_and_sensible_offer_settings()
    {
        var (company, owner) = Company.Create("  Hızlı   Kurye ", Guid.NewGuid(), Builders.Now);

        Assert.Equal(("Hızlı Kurye", Role.Owner), (company.Name, owner.Role));
        Assert.Equal((30, 6.0, 5), (company.OfferSeconds, company.SearchRadiusKm, company.MaxOffers));
    }

    [Theory]
    [InlineData(5, 6, 5)]
    [InlineData(30, 0.1, 5)]
    [InlineData(30, 6, 0)]
    public void Offer_settings_stay_sensible(int seconds, double radius, int offers) =>
        Assert.Throws<DomainException>(() => Company.Create("Hızlı Kurye", Guid.NewGuid(), Builders.Now).Company.Configure(seconds, radius, offers));

    [Fact]
    public void A_team_keeps_an_owner() =>
        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => Company.EnsureAnOwnerRemains(0)).Code);
}
