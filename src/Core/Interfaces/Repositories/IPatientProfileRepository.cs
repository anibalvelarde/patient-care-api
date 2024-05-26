using Neurocorp.Api.Core.BusinessObjects.Patients;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface IPatientProfileRepository : IRepository<PatientProfile>
{
    public Task<PatientProfile> UpdateAsync(int patientId, int userId, PatientProfileUpdateRequest updateRequest);
}