using Neurocorp.Api.Core.BusinessObjects.TreatmentPlans;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface ITreatmentPlanService
{
    Task<TreatmentPlanProfile> CreateAsync(TreatmentPlanRequest request);
    Task<TreatmentPlanProfile?> GetByIdAsync(int id);
    Task<IReadOnlyList<TreatmentPlanProfile>> GetByPatientIdAsync(int patientId);
    Task<TreatmentPlanProfile> UpdateAsync(int id, TreatmentPlanRequest request);
    Task<TreatmentPlanProfile> ActivateAsync(int id);
    Task<TreatmentPlanProfile> CompleteAsync(int id);
    Task<TreatmentPlanProfile> CancelAsync(int id);
    Task<IReadOnlyList<ActivePlanSummary>> GetActiveSummaryAsync();
}
