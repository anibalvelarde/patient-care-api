using Neurocorp.Api.Core.BusinessObjects.Patients;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface IPatientProfileService : IService<PatientProfile>
{
    // other business logic operations...
    public Task<PatientProfile> CreateAsync(PatientProfileRequest request);
    public Task<bool> UpdateAsync(int patientId, PatientProfileUpdateRequest request);
    public Task<bool> VerifyRequestAsync(int patientAggId);
}
