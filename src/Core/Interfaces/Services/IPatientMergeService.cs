using Neurocorp.Api.Core.BusinessObjects.Patients;

namespace Neurocorp.Api.Core.Interfaces.Services;

/// <summary>
/// Duplicate-patient merge (WP-22, SYSADMIN-only): remap every relationship from the
/// eliminated record onto the survivor, enrich the survivor's blank fields, write the audit
/// trail, and hard-delete the eliminated Patient + SystemUser.
/// </summary>
public interface IPatientMergeService
{
    /// <summary>Side-effect-free dry-run; blockers non-empty means Merge would throw ConflictException.</summary>
    Task<PatientMergePreview> PreviewAsync(PatientMergeRequest request);

    /// <summary>Executes the merge in one transaction. Throws ConflictException on any blocker.</summary>
    Task<PatientMergeResult> MergeAsync(PatientMergeRequest request);
}
