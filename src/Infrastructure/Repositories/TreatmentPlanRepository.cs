using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class TreatmentPlanRepository(ApplicationDbContext dbContext) :
    EfRepository<TreatmentPlan>(dbContext), ITreatmentPlanRepository
{
    public async Task<TreatmentPlan?> GetByIdWithLinesAsync(int id)
    {
        return await _dbContext.TreatmentPlans
            .Include(tp => tp.Patient).ThenInclude(p => p!.User)
            .Include(tp => tp.DiscoverySession).ThenInclude(ds => ds!.SpecialtyType)
            .Include(tp => tp.CreatedByTherapist).ThenInclude(t => t!.User)
            .Include(tp => tp.Lines).ThenInclude(l => l.SpecialtyType)
            .Include(tp => tp.Lines).ThenInclude(l => l.PreferredTherapist).ThenInclude(t => t!.User)
            .FirstOrDefaultAsync(tp => tp.Id == id);
    }

    public async Task<IReadOnlyList<TreatmentPlan>> GetByPatientIdAsync(int patientId)
    {
        return await _dbContext.TreatmentPlans
            .Include(tp => tp.Patient).ThenInclude(p => p!.User)
            .Include(tp => tp.DiscoverySession).ThenInclude(ds => ds!.SpecialtyType)
            .Include(tp => tp.CreatedByTherapist).ThenInclude(t => t!.User)
            .Include(tp => tp.Lines).ThenInclude(l => l.SpecialtyType)
            .Include(tp => tp.Lines).ThenInclude(l => l.PreferredTherapist).ThenInclude(t => t!.User)
            .Where(tp => tp.PatientId == patientId)
            .OrderByDescending(tp => tp.CreatedTimestamp)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<TreatmentPlan>> GetActiveWithLinesAsync()
    {
        return await _dbContext.TreatmentPlans
            .Include(tp => tp.Patient).ThenInclude(p => p!.User)
            .Include(tp => tp.DiscoverySession).ThenInclude(ds => ds!.SpecialtyType)
            .Include(tp => tp.CreatedByTherapist).ThenInclude(t => t!.User)
            .Include(tp => tp.Lines).ThenInclude(l => l.SpecialtyType)
            .Include(tp => tp.Lines).ThenInclude(l => l.PreferredTherapist).ThenInclude(t => t!.User)
            .Where(tp => tp.PlanStatus == "Active")
            .OrderByDescending(tp => tp.CreatedTimestamp)
            .ToListAsync();
    }
}
