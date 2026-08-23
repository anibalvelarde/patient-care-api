using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Xunit;

namespace Core.Tests;

/// <summary>
/// WP-39: DataAnnotations on the price-append DTO are what [ApiController] turns into the
/// automatic 400 at the wire: durationMinutes ∈ {30,45,60,90,120} ([AllowedValues]), amount ≥ 0
/// ([Range]), effectiveFrom required. These tests pin the attributes directly (no host needed);
/// live 400 curls are in docs/wp-39-verification.md.
/// </summary>
public class SpecialtyPriceValidationTests
{
    private static IReadOnlyList<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static SpecialtyPriceAppendRow Row(int duration, decimal amount, string? effectiveFrom = "2026-08-01") =>
        new()
        {
            DurationMinutes = duration,
            Amount = amount,
            EffectiveFrom = effectiveFrom is null
                ? null
                : DateOnly.ParseExact(effectiveFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        };

    private static bool HasError(object model, string member) =>
        Validate(model).Any(r => r.MemberNames.Contains(member));

    [Theory]
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(60)]
    [InlineData(90)]
    [InlineData(120)]
    public void Row_Accepts_AllFiveAllowedDurations(int duration)
    {
        Assert.False(HasError(Row(duration, 45.00m), nameof(SpecialtyPriceAppendRow.DurationMinutes)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(31)]
    [InlineData(40)]   // WP-55 G5: 40 removed from the sheet (reverses the WP-40 addendum)
    [InlineData(75)]   // a future 75-min offering is a deliberate value change, not accepted today
    [InlineData(-60)]
    public void Row_Rejects_DurationsOutsideTheSheet(int duration)
    {
        Assert.True(HasError(Row(duration, 45.00m), nameof(SpecialtyPriceAppendRow.DurationMinutes)));
    }

    [Theory]
    [InlineData("0")]      // 0 is a legal price
    [InlineData("45.00")]
    public void Row_Accepts_NonNegativeAmounts(string amount)
    {
        Assert.False(HasError(Row(60, decimal.Parse(amount, CultureInfo.InvariantCulture)),
            nameof(SpecialtyPriceAppendRow.Amount)));
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("-45")]
    public void Row_Rejects_NegativeAmounts(string amount)
    {
        Assert.True(HasError(Row(60, decimal.Parse(amount, CultureInfo.InvariantCulture)),
            nameof(SpecialtyPriceAppendRow.Amount)));
    }

    [Fact]
    public void Row_Rejects_MissingEffectiveFrom()
    {
        Assert.True(HasError(Row(60, 45.00m, effectiveFrom: null),
            nameof(SpecialtyPriceAppendRow.EffectiveFrom)));
    }

    [Fact]
    public void Request_Rejects_EmptyPricesList()
    {
        var request = new SpecialtyPricesPutRequest { Prices = [] };

        Assert.True(HasError(request, nameof(SpecialtyPricesPutRequest.Prices)));
    }

    [Fact]
    public void Request_Accepts_NonEmptyPricesList()
    {
        var request = new SpecialtyPricesPutRequest { Prices = [Row(60, 45.00m)] };

        Assert.False(HasError(request, nameof(SpecialtyPricesPutRequest.Prices)));
    }
}
