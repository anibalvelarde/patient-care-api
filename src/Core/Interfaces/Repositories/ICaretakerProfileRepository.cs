using Neurocorp.Api.Core.BusinessObjects.Patients;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface ICaretakerProfileRepository : IRepository<CaretakerProfile>
{
    public Task<CaretakerProfile> UpdateAsync(int caretakerId, int userId, CaretakerProfileUpdateRequest updateRequest);
}