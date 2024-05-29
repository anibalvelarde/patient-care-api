using Neurocorp.Api.Core.BusinessObjects.Therapists;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface ITherapistProfileRepository : IRepository<TherapistProfile>
{
    public Task<TherapistProfile> UpdateAsync(int patientId, int userId, TherapistProfileUpdateRequest updateRequest);
}