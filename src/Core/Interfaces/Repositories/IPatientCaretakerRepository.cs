using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface IPatientCaretakerRepository
{
    Task<IEnumerable<PatientCaretaker>> GetByCaretakerIdAsync(int caretakerId);
    Task<IEnumerable<PatientCaretaker>> GetByPatientIdAsync(int patientId);
    Task<PatientCaretaker?> GetByCompositeKeyAsync(int patientId, int caretakerId);
    Task AddAsync(PatientCaretaker entity);
    Task DeleteAsync(PatientCaretaker entity);
}
