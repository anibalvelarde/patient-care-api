using Neurocorp.Api.Core.BusinessObjects;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Neurocorp.Api.Core.Interfaces;

public interface IPatientProfileService : IService<PatientProfile>
{
    // other business logic operations...
    public Task<PatientProfile> CreateAsync(PatientProfileRequest request);
    public Task<bool> UpdateAsync(int patientId, PatientProfileUpdateRequest request);
    public Task<bool> VerifyRequestAsync(int patientAggId, PatientProfileUpdateRequest request);
}
