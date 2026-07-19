using Neurocorp.Api.Core.Entities;

namespace Core.Tests;

public class EntityTestsPatient
{
    [Fact]
    public void HasTemporaryMrn_EmptyString_ReturnsTrue()
    {
        var patient = new Patient { MedicalRecordNumber = "" };
        Assert.True(patient.HasTemporaryMrn());
    }

    [Fact]
    public void HasTemporaryMrn_Null_ReturnsTrue()
    {
        var patient = new Patient { MedicalRecordNumber = null! };
        Assert.True(patient.HasTemporaryMrn());
    }

    [Fact]
    public void HasTemporaryMrn_TempPrefix_ReturnsTrue()
    {
        var patient = new Patient { MedicalRecordNumber = "TEMP-42" };
        Assert.True(patient.HasTemporaryMrn());
    }

    [Fact]
    public void HasTemporaryMrn_RealMrn_ReturnsFalse()
    {
        var patient = new Patient { MedicalRecordNumber = "MRN-001" };
        Assert.False(patient.HasTemporaryMrn());
    }

    // ── WP-37 (G1/G2/G3): the single expiry-aware SENADIS predicate ──────────────────
    // flag on AND (expiry null OR session date ≤ expiry); comparison is against the SESSION
    // date, boundary counts as active, and the flag itself is never auto-cleared.

    [Theory]
    [InlineData(false, null, "2026-07-20", false)]         // flag off ⇒ never active
    [InlineData(false, "2027-06-30", "2026-07-20", false)] // flag off beats a future expiry
    [InlineData(true, null, "2026-07-20", true)]           // G1: null = no expiry ⇒ active
    [InlineData(true, "2027-06-30", "2026-07-20", true)]   // session before expiry ⇒ active
    [InlineData(true, "2026-07-20", "2026-07-20", true)]   // boundary: session == expiry ⇒ active
    [InlineData(true, "2026-07-19", "2026-07-20", false)]  // session after expiry ⇒ expired
    public void HasActiveSenadisDiscount_EvaluatesFlagAndExpiryAgainstSessionDate(
        bool flag, string? expiry, string sessionDate, bool expected)
    {
        var patient = new Patient
        {
            HasSenadisDiscount = flag,
            SenadisExpirationDate = expiry is null ? null : DateTime.Parse(expiry),
        };

        Assert.Equal(expected, patient.HasActiveSenadisDiscount(DateOnly.Parse(sessionDate)));
    }
}
