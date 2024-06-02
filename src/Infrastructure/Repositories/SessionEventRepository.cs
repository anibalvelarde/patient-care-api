using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class SessionEventRepository(ApplicationDbContext dbContext) :
    EfRepository<SessionEvent>(dbContext), ISessionEventRepository
{
    public override async Task<IReadOnlyList<SessionEvent>> GetAllAsync()
    {
        var result = await _dbContext.TherapySessions
            .Where(ts => (ts.Patient != null) &&
                         (ts.Therapist != null))
            .Include(ts => ts.Patient)
                .ThenInclude(p => p!.User)
            .Include(ts => ts.Therapist)
                .ThenInclude(t => t!.User)
            .Select(ts => ExtractSessionEvent(ts))
            .ToListAsync();
        return result;
    }

    public async Task<IReadOnlyList<SessionEvent>> GetAllByTargetDateAsync(DateOnly targetDate)
    {
        var result = await _dbContext.TherapySessions
            .Where(ts => (ts.Patient != null) &&
                         (ts.Therapist != null) &&
                         (ts.SessionDate == ConvertToDateTime(targetDate)))
            .Include(ts => ts.Patient)
                .ThenInclude(p => p!.User)
            .Include(ts => ts.Therapist)
                .ThenInclude(t => t!.User)
            .Select(ts => ExtractSessionEvent(ts))
            .ToListAsync();
        return result;        
    }

    public async Task<IReadOnlyList<SessionEvent>> GetAllPastDueAsync()
    {
        var result = await _dbContext.TherapySessions
            .Where(ts => (ts.Patient != null) &&
                         (ts.Therapist != null))
            .Include(ts => ts.Patient)
                .ThenInclude(p => p!.User)
            .Include(ts => ts.Therapist)
                .ThenInclude(t => t!.User)
            .Select(ts => ExtractSessionEvent(ts))
            .ToListAsync();
        return result;        
    }    

    public override async Task<SessionEvent?> GetByIdAsync(int id)
    {
        var result = await _dbContext.TherapySessions
        .Where(ts => ts.Id == id)
            .Include(ts => ts.Patient)
                .ThenInclude(p => p!.User)
            .Include(ts => ts.Therapist)
                .ThenInclude(t => t!.User)
            .Select(ts => ExtractSessionEvent(ts))
            .ToListAsync();
        return result.FirstOrDefault();
    }

    public override async Task<SessionEvent> AddAsync(SessionEvent sessionEvent)
    {
        return await Task.FromException<SessionEvent>(new NotImplementedException());
    }

    public async Task<SessionEvent> UpdateAsync(int therapySessionId, SessionEventUpdateRequest updateRequest)
    {
        // fetch the therapy session
        var therapySessionToUpdate = await _dbContext.TherapySessions
            .Include(ts => ts.Patient)
                .ThenInclude(p => p!.User)
            .Include(ts => ts.Therapist)
                .ThenInclude(t => t!.User)
            .FirstAsync(ts => ts.Id == therapySessionId);
        therapySessionToUpdate = MapUpdatedTherapySession(updateRequest, therapySessionToUpdate);
        _dbContext.ChangeTracker.DetectChanges();
        await _dbContext.SaveChangesAsync();
        
        return ExtractSessionEvent(therapySessionToUpdate);
    }

    private TherapySession MapUpdatedTherapySession(SessionEventUpdateRequest updateRequest, TherapySession therapySessionToUpdate)
    {

        if (updateRequest.SessionDate is not null
            && updateRequest.SessionDate.HasValue
            && !updateRequest.SessionDate.Equals(DateOnly.FromDateTime(therapySessionToUpdate.SessionDate)))
        {
            therapySessionToUpdate.SessionDate = updateRequest.SessionDate.Value.ToDateTime(new TimeOnly(0, 0));
        }
        therapySessionToUpdate.Duration = updateRequest.Duration;
        therapySessionToUpdate.Amount = updateRequest.Amount;
        therapySessionToUpdate.AmountPaid = updateRequest.AmountPaid;
        therapySessionToUpdate.DiscountAmount = updateRequest.Discount;
        therapySessionToUpdate.ProviderAmount = updateRequest.ProviderAmount;
        therapySessionToUpdate.Notes = updateRequest.Notes;

        return therapySessionToUpdate;
    }

    private static SessionEvent ExtractSessionEvent(TherapySession ts)
    {
        ArgumentNullException.ThrowIfNull(ts, nameof(ts) + " must not be null");

        return new SessionEvent
        {
            SessionId = ts.Id,
            PatientId = ts.PatientId,
            TherapistId = ts.TherapistId,
            SessionDate = DateOnly.FromDateTime(ts.SessionDate),
            Patient = ts.Patient!.User!.GetFullName(),
            Therapist = ts.Therapist!.User!.GetFullName(),
            TherapyTypes = ts.TherapyTypes,
            Amount = ts.Amount,
            Discount = ts.DiscountAmount,
            AmountPaid = ts.AmountPaid,
            AmountDue = ts.AmountDue(),
            IsPaidOff = ts.IsPaidOff,
            IsPastDue = ts.GetPastDue(),
            Notes = ts.Notes,
        };
    }
    private static DateTime ConvertToDateTime(DateOnly? dateOnly)
    {
        if (dateOnly.HasValue)
        {
            // Convert DateOnly to DateTime with a specified time part (e.g., midnight)
            return dateOnly.Value.ToDateTime(new TimeOnly(0, 0));
        }
        else
        {
            // Handle the case when dateOnly is null (you can set a default value or handle accordingly)
            return DateTime.MinValue; // or any other default value
        }
    }    
}