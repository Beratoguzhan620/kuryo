using Kuryo.Core;
using Kuryo.Core.Couriers;
using Kuryo.Core.Geo;
using Kuryo.Core.Offers;

namespace Kuryo.UnitTests;

public sealed class OfferTests
{
    private static readonly DateTimeOffset Now = Builders.Now;

    private static Offer Make(int seconds = 30) => Offer.Make(Builders.CompanyId, Guid.NewGuid(), Builders.Courier, 1.234, seconds, Now);

    [Fact]
    public void An_offer_is_pending_until_its_time_runs_out()
    {
        var offer = Make();

        Assert.Equal((OfferStatus.Pending, Now.AddSeconds(30), 1.23), (offer.Status, offer.ExpiresAt, offer.DistanceKm));
        Assert.True(offer.IsLive(Now.AddSeconds(29)));
        Assert.False(offer.IsLive(Now.AddSeconds(30)));
    }

    [Fact]
    public void Accepting_in_time_takes_the_order()
    {
        var offer = Make();

        offer.Accept(Now.AddSeconds(10));

        Assert.Equal((OfferStatus.Accepted, Now.AddSeconds(10)), (offer.Status, offer.RespondedAt));
    }

    [Fact]
    public void An_offer_cannot_be_answered_after_it_runs_out_even_before_the_matcher_marks_it()
    {
        var offer = Make();

        var late = Assert.Throws<DomainException>(() => offer.Accept(Now.AddSeconds(31)));

        Assert.Equal(ErrorCodes.OfferGone, late.Code);
        Assert.Contains("too late", late.Message);
        Assert.Equal(OfferStatus.Pending, offer.Status);
    }

    [Fact]
    public void An_offer_is_answered_once()
    {
        var offer = Make();
        offer.Decline(Now.AddSeconds(5));

        var again = Assert.Throws<DomainException>(() => offer.Accept(Now.AddSeconds(6)));

        Assert.Equal((ErrorCodes.OfferGone, "This offer was already declined."), (again.Code, again.Message));
    }

    [Fact]
    public void Withdrawing_only_touches_a_pending_offer()
    {
        var accepted = Make();
        accepted.Accept(Now.AddSeconds(1));
        var pending = Make();

        accepted.Withdraw(Now.AddSeconds(2));
        pending.Withdraw(Now.AddSeconds(2));

        Assert.Equal((OfferStatus.Accepted, OfferStatus.Withdrawn), (accepted.Status, pending.Status));
    }

    [Theory]
    [InlineData(-5, -5)]
    [InlineData(30, 0)]
    [InlineData(-60, -10)]
    public void Phone_times_are_kept_between_ten_minutes_ago_and_now(int minutes, int expected)
    {
        Assert.Equal(Now.AddMinutes(expected), Position.Clamp(Now.AddMinutes(minutes), Now));
    }

    [Fact]
    public void A_courier_is_known_near_only_with_a_recent_location()
    {
        var courier = Courier.Create(Builders.CompanyId, Builders.Courier, Vehicle.Scooter, null, Now);
        Assert.False(courier.KnownNear(Now));

        courier.MoveTo(GeoPoint.At(41.29, 36.33), Now);

        Assert.True(courier.KnownNear(Now.AddMinutes(2)));
        Assert.False(courier.KnownNear(Now.AddMinutes(2).AddSeconds(1)));
    }

    [Fact]
    public void A_search_ends_after_the_offers_or_the_time_they_would_take()
    {
        var order = Builders.Order();

        Assert.False(order.SearchIsOver(maxOffers: 2, offerSeconds: 30, Now.AddSeconds(59)));
        Assert.True(order.SearchIsOver(maxOffers: 2, offerSeconds: 30, Now.AddSeconds(60)));

        order.Offered(Now);
        order.Offered(Now);
        Assert.True(order.SearchIsOver(maxOffers: 2, offerSeconds: 30, Now));
    }

    [Fact]
    public void Searching_again_restarts_the_clock_and_the_count()
    {
        var order = Builders.Order();
        order.Offered(Now);
        order.GiveUp(Now.AddMinutes(3));

        order.SearchAgain(Now.AddMinutes(5));

        Assert.Equal((0, Now.AddMinutes(5)), (order.OffersMade, order.SearchStartedAt));
        Assert.False(order.SearchIsOver(5, 30, Now.AddMinutes(6)));
    }
}
