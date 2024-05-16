using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Neurocorp.Api.Core.Services;

public class PatientService : IPatientService
{
    private readonly IPatientRepository _patientRepository;

    public PatientService(IPatientRepository patientRepository)
    {
        _patientRepository = patientRepository;
    }

    public async Task<IEnumerable<Patient>> GetAllAsync()
    {
        return await _patientRepository.GetAllAsync();
    }

    public async Task<Patient?> GetByIdAsync(int id)
    {
        var p = await _patientRepository.GetByIdAsync(id);
        if (p == null)
        {
            return new UndefinedPatient();
        }
        return p;
    }

    public async Task<Patient> CreateAsync(Patient patient)
    {
        return await _patientRepository.AddAsync(patient);
    }

    public async Task UpdateAsync(Patient patient)
    {
        await _patientRepository.UpdateAsync(patient);
    }

    public async Task DeleteAsync(int id)
    {
        var patient = await _patientRepository.GetByIdAsync(id);
        if (patient != null)
        {
            await _patientRepository.DeleteAsync(patient);
        }
    }
}
