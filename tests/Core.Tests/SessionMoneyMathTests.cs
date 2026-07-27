using Neurocorp.Api.Core.Services;

namespace Core.Tests;

// WP-40: pins the shared booking-money rules both money paths (single + bulk) derive from.
public class SessionMoneyMathTests
{
    [Theory]
    [InlineData(30, true)]
    [InlineData(40, true)]  // 2026-07-27 addendum: 40-min interview services are real
    [InlineData(45, true)]
    [InlineData(60, true)]
    [InlineData(90, true)]
    [InlineData(120, true)]
    [InlineData(15, false)]
    [InlineData(50, false)]
    [InlineData(75, false)]
    [InlineData(0, false)]
    public void IsBookableDuration_MatchesTheFixedSet(int minutes, bool expected)
        => Assert.Equal(expected, SessionMoneyMath.IsBookableDuration(minutes));

    [Fact]
    public void BookableDurations_AreExactlyThePriceSheetDurations()
        => Assert.Equal([30, 40, 45, 60, 90, 120], SpecialtyPricing.AllowedDurations);

    [Theory]
    [InlineData(100, true, 20)]     // active SENADIS ⇒ exactly 20%
    [InlineData(100, false, 0)]     // else exactly 0
    [InlineData(33.33, true, 6.67)] // 2dp rounding
    public void DeriveBookingDiscount_ExactTwentyOrZero(decimal amount, bool active, decimal expected)
        => Assert.Equal(expected, SessionMoneyMath.DeriveBookingDiscount(amount, active));

    [Fact]
    public void SenadisFloor_IsTwentyPercentRounded()
        => Assert.Equal(13.00m, SessionMoneyMath.SenadisFloor(65.00m));

    [Theory]
    [InlineData(25, 0, 80, 25)]      // pct 0 ⇒ flat fee, whatever the net
    [InlineData(25, 0.5, 80, 40)]    // pct set ⇒ % of net, flat ignored
    [InlineData(0, 0, 80, 0)]        // no fee model ⇒ 0
    public void ProviderFee_FlatWhenPctZero_ElsePercentOfNet(
        decimal flat, decimal pct, decimal net, decimal expected)
        => Assert.Equal(expected, SessionMoneyMath.ProviderFee(flat, pct, net));

    [Theory]
    [InlineData(80, 40, null, 40)]  // no on-site charge: net − fee
    [InlineData(80, 40, 25.0, 65)]  // charge adds to gross profit…
    public void GrossProfit_AddsOnSiteCharge_NeverToFeeBase(
        decimal net, decimal fee, double? charge, decimal expected)
        => Assert.Equal(expected, SessionMoneyMath.GrossProfit(net, fee, (decimal?)charge));

    [Fact]
    public void MissingPriceMessage_IsTheContractCopy()
        => Assert.Equal(
            "No price configured for Neuropediatría at 40 min — ask a manager to set it in Admin.",
            SessionMoneyMath.MissingPriceMessage("Neuropediatría", 40));
}
