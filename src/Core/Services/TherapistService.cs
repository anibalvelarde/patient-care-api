using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Neurocorp.Api.Core.Services;

public class TherapistService : ITherapistService
{
    private readonly ITherapistRepository _repository;

    public TherapistService(ITherapistRepository therapistRepository)
    {
        _repository = therapistRepository;
    }

    public async Task<IEnumerable<Therapist>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<Therapist?> GetByIdAsync(int id)
    {
        var p = await _repository.GetByIdAsync(id);
        if (p == null)
        {
            return new UndefinedTherapist();
        }
        return p;
    }

    public async Task<Therapist> CreateAsync(Therapist therapist)
    {
        return await _repository.AddAsync(therapist);
    }

    public async Task UpdateAsync(Therapist threapist)
    {
        await _repository.UpdateAsync(threapist);
    }

    public async Task DeleteAsync(int id)
    {
        var therapist = await _repository.GetByIdAsync(id);
        if (therapist != null)
        {
            await _repository.DeleteAsync(therapist);
        }
    }
}
