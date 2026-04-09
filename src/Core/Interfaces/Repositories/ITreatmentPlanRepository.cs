using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface ITreatmentPlanRepository : IRepository<TreatmentPlan>
{
    Task<TreatmentPlan?> GetByIdWithLinesAsync(int id);
    Task<IReadOnlyList<TreatmentPlan>> GetByPatientIdAsync(int patientId);
    Task<IReadOnlyList<TreatmentPlan>> GetActiveWithLinesAsync();
}
