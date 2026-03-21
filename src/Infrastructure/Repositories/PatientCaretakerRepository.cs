using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class PatientCaretakerRepository : IPatientCaretakerRepository
{
    private readonly ApplicationDbContext _dbContext;

    public PatientCaretakerRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<PatientCaretaker>> GetByCaretakerIdAsync(int caretakerId)
    {
        return await _dbContext.Set<PatientCaretaker>()
            .Where(pc => pc.CaretakerId == caretakerId)
            .Include(pc => pc.Patient)
                .ThenInclude(p => p!.User)
            .Include(pc => pc.Caretaker)
                .ThenInclude(c => c!.User)
            .ToListAsync();
    }

    public async Task<IEnumerable<PatientCaretaker>> GetByPatientIdAsync(int patientId)
    {
        return await _dbContext.Set<PatientCaretaker>()
            .Where(pc => pc.PatientId == patientId)
            .Include(pc => pc.Patient)
                .ThenInclude(p => p!.User)
            .Include(pc => pc.Caretaker)
                .ThenInclude(c => c!.User)
            .ToListAsync();
    }

    public async Task<PatientCaretaker?> GetByCompositeKeyAsync(int patientId, int caretakerId)
    {
        return await _dbContext.Set<PatientCaretaker>()
            .FirstOrDefaultAsync(pc => pc.PatientId == patientId && pc.CaretakerId == caretakerId);
    }

    public async Task AddAsync(PatientCaretaker entity)
    {
        await _dbContext.Set<PatientCaretaker>().AddAsync(entity);
        await _dbContext.SaveChangesAsync();
    }

    public async Task DeleteAsync(PatientCaretaker entity)
    {
        _dbContext.Set<PatientCaretaker>().Remove(entity);
        await _dbContext.SaveChangesAsync();
    }
}
