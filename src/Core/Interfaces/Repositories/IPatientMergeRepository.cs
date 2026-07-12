using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

/// <summary>
/// Data access for the duplicate-patient merge (WP-22). Deliberately its own interface: the
/// merge needs by-user reads (roles, claims, therapist/caretaker identity checks) and bulk FK
/// repoints that no existing repository exposes. All mutating members are called inside the
/// merge's single IUnitOfWork transaction.
/// </summary>
public interface IPatientMergeRepository
{
    Task<Patient?> GetPatientWithUserAsync(int patientId);
    Task<IReadOnlyList<UserRole>> GetUserRolesAsync(int userId);
    Task<bool> IsTherapistUserAsync(int userId);
    Task<bool> IsCaretakerUserAsync(int userId);
    /// <summary>Caretaker links for a patient, with Caretaker.User and Caretaker.Notes loaded.</summary>
    Task<IReadOnlyList<PatientCaretaker>> GetCaretakerLinksAsync(int patientId);
    Task<int> CountSessionsAsync(int patientId);
    Task<int> CountTreatmentPlansAsync(int patientId);

    /// <summary>Bulk-repoints TherapySession.PatientID (ExecuteUpdate — MySQL only, not InMemory).</summary>
    Task<int> ReassignSessionsAsync(int fromPatientId, int toPatientId, int mergedByUserId);
    /// <summary>Bulk-repoints TreatmentPlan.PatientID (ExecuteUpdate — MySQL only, not InMemory).</summary>
    Task<int> ReassignTreatmentPlansAsync(int fromPatientId, int toPatientId, int mergedByUserId);

    Task UpdateCaretakerLinkAsync(PatientCaretaker link);
    Task DeleteCaretakerLinkAsync(PatientCaretaker link);
    Task DeleteCaretakerAsync(Caretaker caretaker);
    Task UpdatePatientAsync(Patient patient);
    Task DeletePatientAsync(Patient patient);
    /// <summary>Retires a SystemUser: deletes its UserRole rows, then UserClaim rows, then the user.</summary>
    Task DeleteUserIdentityAsync(int userId);
    Task<PatientMergeLog> AddMergeLogAsync(PatientMergeLog log);
}
