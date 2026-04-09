namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface ITherapistSpecialtyRepository
{
    Task SetSpecialtiesAsync(int therapistId, IEnumerable<int> specialtyIds);
    Task<IEnumerable<int>> GetValidSpecialtyIdsAsync(IEnumerable<int> candidateIds);
    Task<IReadOnlyList<int>> GetTherapistIdsBySpecialtyAsync(int specialtyId);
    Task<bool> HasTherapistSpecialtyAsync(int therapistId, int specialtyId);
}
