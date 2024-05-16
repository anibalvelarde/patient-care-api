using Neurocorp.Api.Core.BusinessObjects;
using Neurocorp.Api.Core.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Neurocorp.Api.Core.Services;

public class PatientProfileService : IPatientProfileService
{
    private readonly IPatientProfileRepository _repository;

    public PatientProfileService(IPatientProfileRepository patientProfileRepository)
    {
        _repository = patientProfileRepository;
    }

    public async Task<IEnumerable<PatientProfile>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<PatientProfile> GetByIdAsync(int id)
    {
        var p = await _repository.GetByIdAsync(id);
        if (p == null)
        {
            return new UndefinedPatientProfile();
        }
        return p;
    }

    public async Task<PatientProfile> CreateAsync(PatientProfile patient)
    {
        return await _repository.AddAsync(patient);
    }

    public async Task UpdateAsync(PatientProfile patient)
    {
        await _repository.UpdateAsync(patient);
    }

    public async Task DeleteAsync(int id)
    {
        var profile = await _repository.GetByIdAsync(id);
        if (profile != null)
        {
            await _repository.DeleteAsync(profile);
        }
    }
}
