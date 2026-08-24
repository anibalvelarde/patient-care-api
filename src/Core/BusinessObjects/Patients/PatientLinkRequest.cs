using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.BusinessObjects.Patients;

public class PatientLinkRequest
{
    public int PatientId { get; set; }
    public bool IsPrimary { get; set; }

    // WP-55 B-2b: null/omitted = unspecified (DB column is nullable); any non-null value must be a
    // real relationship or the request 400s. Before this an unknown value truncated into the MySQL
    // enum column and surfaced as a generic 500.
    [AllowedValues(null, CaretakerRelationships.Father, CaretakerRelationships.Mother,
        CaretakerRelationships.Relative, CaretakerRelationships.HiredHelp,
        CaretakerRelationships.LegalGuardian, CaretakerRelationships.Self)]
    public string? Relationship { get; set; }
}
