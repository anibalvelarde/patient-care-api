using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Neurocorp.Api.Core.Services;

public class TherapySessionService : ITherapySessionService
{
    private readonly ITherapySessionRepository _repository;

    public TherapySessionService(ITherapySessionRepository patientRepository)
    {
        _repository = patientRepository;
    }

    public async Task<IEnumerable<TherapySession>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<TherapySession?> GetByIdAsync(int id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task<TherapySession> CreateAsync(TherapySession session)
    {
        return await _repository.AddAsync(session);
    }

    public async Task UpdateAsync(TherapySession session)
    {
        await _repository.UpdateAsync(session);
    }

    public async Task DeleteAsync(int id)
    {
        var session = await _repository.GetByIdAsync(id);
        if (session != null)
        {
            await _repository.DeleteAsync(session);
        }
    }
}
