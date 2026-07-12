using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Neurocorp.Api.Infrastructure.Repositories;

/// <summary>
/// Data access for the duplicate-patient merge (WP-22). All members run on the ambient
/// DbContext so the service's IUnitOfWork transaction covers every statement — including the
/// two ExecuteUpdate bulk repoints (relational providers only; the InMemory test provider
/// throws on them, so those two members are verified against real MySQL via
/// docs/patient-merge-wp22-verification.md and the batch invariant queries).
/// </summary>
public class PatientMergeRepository : IPatientMergeRepository
{
    private readonly ApplicationDbContext _dbContext;

    public PatientMergeRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Patient?> GetPatientWithUserAsync(int patientId)
    {
        return await _dbContext.Patients
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == patientId);
    }

    public async Task<IReadOnlyList<UserRole>> GetUserRolesAsync(int userId)
    {
        return await _dbContext.Set<UserRole>()
            .Where(r => r.UserId == userId)
            .ToListAsync();
    }

    public async Task<bool> IsTherapistUserAsync(int userId)
    {
        return await _dbContext.Therapists.AnyAsync(t => t.User!.Id == userId);
    }

    public async Task<bool> IsCaretakerUserAsync(int userId)
    {
        return await _dbContext.Caretakers.AnyAsync(c => c.User!.Id == userId);
    }

    public async Task<IReadOnlyList<PatientCaretaker>> GetCaretakerLinksAsync(int patientId)
    {
        return await _dbContext.Set<PatientCaretaker>()
            .Where(pc => pc.PatientId == patientId)
            .Include(pc => pc.Caretaker)
                .ThenInclude(c => c!.User)
            .ToListAsync();
    }

    public async Task<int> CountSessionsAsync(int patientId)
    {
        return await _dbContext.TherapySessions.CountAsync(s => s.PatientId == patientId);
    }

    public async Task<int> CountTreatmentPlansAsync(int patientId)
    {
        return await _dbContext.TreatmentPlans.CountAsync(p => p.PatientId == patientId);
    }

    // ExecuteUpdate bypasses SaveChanges interception, so the audit columns are stamped
    // explicitly in the SET clause.
    public async Task<int> ReassignSessionsAsync(int fromPatientId, int toPatientId, int mergedByUserId)
    {
        return await _dbContext.TherapySessions
            .Where(s => s.PatientId == fromPatientId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.PatientId, toPatientId)
                .SetProperty(s => s.LastUpdatedTimestamp, DateTime.UtcNow)
                .SetProperty(s => s.LastUpdatedByUserId, mergedByUserId));
    }

    public async Task<int> ReassignTreatmentPlansAsync(int fromPatientId, int toPatientId, int mergedByUserId)
    {
        return await _dbContext.TreatmentPlans
            .Where(p => p.PatientId == fromPatientId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(p => p.PatientId, toPatientId)
                .SetProperty(p => p.LastUpdatedTimestamp, DateTime.UtcNow)
                .SetProperty(p => p.LastUpdatedByUserId, mergedByUserId));
    }

    public async Task UpdateCaretakerLinkAsync(PatientCaretaker link)
    {
        _dbContext.Entry(link).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync();
    }

    public async Task DeleteCaretakerLinkAsync(PatientCaretaker link)
    {
        _dbContext.Set<PatientCaretaker>().Remove(link);
        await _dbContext.SaveChangesAsync();
    }

    public async Task DeleteCaretakerAsync(Caretaker caretaker)
    {
        _dbContext.Caretakers.Remove(caretaker);
        await _dbContext.SaveChangesAsync();
    }

    public async Task UpdatePatientAsync(Patient patient)
    {
        _dbContext.Entry(patient).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync();
    }

    public async Task DeletePatientAsync(Patient patient)
    {
        _dbContext.Patients.Remove(patient);
        await _dbContext.SaveChangesAsync();
    }

    // Fix-b1 retirement order: role rows, then claim rows, then the SystemUser itself.
    public async Task DeleteUserIdentityAsync(int userId)
    {
        var roles = await _dbContext.Set<UserRole>().Where(r => r.UserId == userId).ToListAsync();
        _dbContext.Set<UserRole>().RemoveRange(roles);
        await _dbContext.SaveChangesAsync();

        var claims = await _dbContext.UserClaims.Where(c => c.UserId == userId).ToListAsync();
        _dbContext.UserClaims.RemoveRange(claims);
        await _dbContext.SaveChangesAsync();

        var user = await _dbContext.Users.FindAsync(userId);
        if (user is not null)
        {
            _dbContext.Users.Remove(user);
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task<PatientMergeLog> AddMergeLogAsync(PatientMergeLog log)
    {
        await _dbContext.PatientMergeLogs.AddAsync(log);
        await _dbContext.SaveChangesAsync();
        return log;
    }
}
