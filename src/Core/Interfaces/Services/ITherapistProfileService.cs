using Neurocorp.Api.Core.BusinessObjects.Therapists;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface ITherapistProfileService : IService<TherapistProfile>
{
    // other business logic operations...
    public Task<TherapistProfile> CreateAsync(TherapistProfileRequest request);
    public Task<bool> UpdateAsync(int patientId, TherapistProfileUpdateRequest request);
    public Task<bool> VerifyRequestAsync(int patientAggId);
}
