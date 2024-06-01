using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface ICaretakerProfileService : IService<CaretakerProfile>
{
    // other business logic operations...
    public Task<CaretakerProfile> CreateAsync(CaretakerProfileRequest request);
    public Task<bool> UpdateAsync(int caretakerId, CaretakerProfileUpdateRequest request);
    public Task<bool> VerifyRequestAsync(int caretakerAggId, CaretakerProfileUpdateRequest request);
}
