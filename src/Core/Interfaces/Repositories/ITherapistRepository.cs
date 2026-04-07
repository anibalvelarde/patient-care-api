using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface ITherapistRepository : IRepository<Therapist>
{
    Task<IReadOnlyList<Therapist>> GetByIdsWithUserAsync(IEnumerable<int> therapistIds);
}