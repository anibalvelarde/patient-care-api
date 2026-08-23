using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Xunit;

namespace Core.Tests;

/// <summary>
/// WP-55 B-2b / B-2e: DataAnnotations that [ApiController] turns into an automatic 400 at the wire.
/// Before WP-55 these fields were unvalidated passthroughs into MySQL enum columns — an unknown
/// value truncated on insert and surfaced as a generic 500 instead of a clean 400.
/// </summary>
public class Wp55ValidationTests
{
    private static bool HasError(object model, string member)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results.Any(r => r.MemberNames.Contains(member));
    }

    // ---- B-2b: PatientLinkRequest.Relationship --------------------------------------------------
    [Theory]
    [InlineData("Self")]
    [InlineData("Father")]
    [InlineData("Mother")]
    [InlineData("Relative")]
    [InlineData("HiredHelp")]
    [InlineData("Legal-Guardian")]
    [InlineData(null)]   // optional — DB column is nullable
    public void Relationship_Accepts_ValidValuesAndNull(string? relationship)
        => Assert.False(HasError(new PatientLinkRequest { PatientId = 1, Relationship = relationship },
            nameof(PatientLinkRequest.Relationship)));

    [Theory]
    [InlineData("Cousin")]
    [InlineData("Guardian")]
    [InlineData("self")]   // case-sensitive: MySQL enum values are exact
    [InlineData("")]
    public void Relationship_Rejects_UnknownValues(string relationship)
        => Assert.True(HasError(new PatientLinkRequest { PatientId = 1, Relationship = relationship },
            nameof(PatientLinkRequest.Relationship)));

    // ---- B-2e: ConfirmationRequest.ConfirmationResult -------------------------------------------
    [Theory]
    [InlineData("Confirmed")]
    [InlineData("NoAnswer")]
    [InlineData("LeftMessage")]
    [InlineData("Declined")]
    public void ConfirmationResult_Accepts_ValidValues(string result)
        => Assert.False(HasError(new ConfirmationRequest { ConfirmationResult = result },
            nameof(ConfirmationRequest.ConfirmationResult)));

    [Theory]
    [InlineData("Maybe")]
    [InlineData("confirmed")]
    [InlineData("")]
    public void ConfirmationResult_Rejects_UnknownValues(string result)
        => Assert.True(HasError(new ConfirmationRequest { ConfirmationResult = result },
            nameof(ConfirmationRequest.ConfirmationResult)));
}
