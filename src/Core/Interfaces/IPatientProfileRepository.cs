using Neurocorp.Api.Core.BusinessObjects;

namespace Neurocorp.Api.Core.Interfaces;

public interface IPatientProfileRepository : IRepository<PatientProfile>
{
    public Task<PatientProfile> UpdateAsync(int patientId, int userId, PatientProfileUpdateRequest updateRequest);
}