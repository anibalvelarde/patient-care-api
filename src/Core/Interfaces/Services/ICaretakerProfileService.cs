using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface ICaretakerProfileService : IService<CaretakerProfile>
{
    // other business logic operations...
    public Task<CaretakerProfile> CreateAsync(CaretakerProfileRequest request);
    public Task<bool> UpdateAsync(int caretakerId, CaretakerProfileUpdateRequest request);
    public Task<bool> VerifyRequestAsync(int caretakerAggId, CaretakerProfileUpdateRequest request);
    Task<IEnumerable<CaretakerPatientSummary>> GetPatientsForCaretakerAsync(int caretakerId);
    Task<bool> LinkPatientAsync(int caretakerId, int patientId, bool isPrimary, string? relationship);
    Task<bool> UnlinkPatientAsync(int caretakerId, int patientId);
}
