using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Xunit;
using Neurocorp.Api.Core.BusinessObjects.Sites;

namespace Core.Tests;

/// <summary>
/// WP-32 (U4), gate G3: IdleLogoffMinutes must be clamped 0-480 in API validation. The
/// [Range(0,480)] attribute is what [ApiController] turns into an automatic 400 at the wire;
/// these tests pin the attribute directly (no running host needed). Live 400 curls are in
/// docs/wp-32-verification.md.
/// </summary>
public class SiteProfileValidationTests
{
    private static IReadOnlyList<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static bool HasIdleLogoffError(IReadOnlyList<ValidationResult> results)
    {
        foreach (var r in results)
        {
            foreach (var member in r.MemberNames)
            {
                if (member == nameof(SiteProfileRequest.IdleLogoffMinutes)) return true;
            }
        }
        return false;
    }

    [Theory]
    [InlineData(0)]     // disabled — valid boundary
    [InlineData(60)]
    [InlineData(480)]   // upper boundary — valid
    public void Request_Accepts_InRangeIdleLogoffMinutes(int minutes)
    {
        var model = new SiteProfileRequest
        {
            SiteName = "Clinic",
            InceptionDate = new System.DateTime(2025, 1, 1),
            IdleLogoffMinutes = minutes
        };

        Assert.False(HasIdleLogoffError(Validate(model)));
    }

    [Fact]
    public void Request_Accepts_NullIdleLogoffMinutes()
    {
        var model = new SiteProfileRequest
        {
            SiteName = "Clinic",
            InceptionDate = new System.DateTime(2025, 1, 1),
            IdleLogoffMinutes = null
        };

        Assert.False(HasIdleLogoffError(Validate(model)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(481)]
    [InlineData(9999)]
    public void Request_Rejects_OutOfRangeIdleLogoffMinutes(int minutes)
    {
        var model = new SiteProfileRequest
        {
            SiteName = "Clinic",
            InceptionDate = new System.DateTime(2025, 1, 1),
            IdleLogoffMinutes = minutes
        };

        Assert.True(HasIdleLogoffError(Validate(model)));
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(481)]
    public void UpdateRequest_Rejects_OutOfRangeIdleLogoffMinutes(int minutes)
    {
        var model = new SiteProfileUpdateRequest { IdleLogoffMinutes = minutes };

        Assert.True(HasIdleLogoffError(Validate(model)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(480)]
    public void UpdateRequest_Accepts_InRangeIdleLogoffMinutes(int minutes)
    {
        var model = new SiteProfileUpdateRequest { IdleLogoffMinutes = minutes };

        Assert.False(HasIdleLogoffError(Validate(model)));
    }

    // --- WP-39 (G4): OnSiteTripChargeAmount must be ≥ 0 (auto-400 via [Range]) ---

    private static bool HasTripChargeError(IReadOnlyList<ValidationResult> results)
    {
        foreach (var r in results)
        {
            foreach (var member in r.MemberNames)
            {
                if (member == nameof(SiteProfileRequest.OnSiteTripChargeAmount)) return true;
            }
        }
        return false;
    }

    [Theory]
    [InlineData("0")]      // 0 = no charge configured — valid boundary
    [InlineData("15.50")]
    public void Request_Accepts_NonNegativeTripCharge(string amount)
    {
        var model = new SiteProfileRequest
        {
            SiteName = "Clinic",
            InceptionDate = new System.DateTime(2025, 1, 1),
            OnSiteTripChargeAmount = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)
        };

        Assert.False(HasTripChargeError(Validate(model)));
    }

    [Fact]
    public void Request_Accepts_NullTripCharge()
    {
        var model = new SiteProfileRequest
        {
            SiteName = "Clinic",
            InceptionDate = new System.DateTime(2025, 1, 1),
            OnSiteTripChargeAmount = null
        };

        Assert.False(HasTripChargeError(Validate(model)));
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("-25")]
    public void Request_Rejects_NegativeTripCharge(string amount)
    {
        var model = new SiteProfileRequest
        {
            SiteName = "Clinic",
            InceptionDate = new System.DateTime(2025, 1, 1),
            OnSiteTripChargeAmount = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)
        };

        Assert.True(HasTripChargeError(Validate(model)));
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("-25")]
    public void UpdateRequest_Rejects_NegativeTripCharge(string amount)
    {
        var model = new SiteProfileUpdateRequest
        {
            OnSiteTripChargeAmount = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)
        };

        Assert.True(HasTripChargeError(Validate(model)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("25.00")]
    public void UpdateRequest_Accepts_NonNegativeTripCharge(string amount)
    {
        var model = new SiteProfileUpdateRequest
        {
            OnSiteTripChargeAmount = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)
        };

        Assert.False(HasTripChargeError(Validate(model)));
    }
}
