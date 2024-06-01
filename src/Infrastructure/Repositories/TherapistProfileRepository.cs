using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.BusinessObjects.Therapists;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.BusinessObjects;
using System.Linq;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class TherapistProfileRepository(ApplicationDbContext dbContext) :
    EfRepository<TherapistProfile>(dbContext), ITherapistProfileRepository
{

    // Additional methods specific to Patient can be implemented here
    public override async Task<IReadOnlyList<TherapistProfile>> GetAllAsync()
    {
        var result = await _dbContext.Therapists
            .Where(t => t.User != null)
            .Include(t => t.User)
            .Select(t => ExtractTherapistProfile(t)).ToListAsync();
        return result;
    }

    public override async Task<TherapistProfile?> GetByIdAsync(int id)
    {
        var result = await _dbContext.Therapists
        .Where(t => t.TherapistId == id)
        .Include(t => t.User)
        .Select(t => ExtractTherapistProfile(t))
        .ToListAsync();

        return result.FirstOrDefault();
    }

    public override async Task<TherapistProfile> AddAsync(TherapistProfile entity)
    {
        return await Task.FromException<TherapistProfile>(new NotImplementedException());
    }

    public override async Task<TherapistProfile> UpdateAsync(TherapistProfile entity)
    {
        return await Task.FromException<TherapistProfile>(new NotImplementedException());
    }

    public async Task<TherapistProfile> UpdateAsync(int therapistId, int userId, TherapistProfileUpdateRequest updateRequest)
    {
        // fetch the entities & ensure they are valid...
        var therapist = await _dbContext.Therapists
            .Include(p => p.User)
            .FirstAsync(t => t.TherapistId == therapistId);

        // update entity props & save changes.
        therapist = MapToUpdatedTherapist(updateRequest, therapist);
        therapist.User = MapToUpdatedUser(updateRequest, therapist.User ?? new User());
        _dbContext.ChangeTracker.DetectChanges();
        await _dbContext.SaveChangesAsync();

        return ExtractTherapistProfile(therapist);
    }


    private static Therapist MapToUpdatedTherapist(TherapistProfileUpdateRequest therapistRequest, Therapist therapistOnFile)
    {
        therapistOnFile.FeePctPerSession = therapistRequest.FeePctPerSession ?? 0m;
        therapistOnFile.FeePerSession = therapistRequest.FeePerSession ?? 0m;
        return therapistOnFile;
    }

    private static User MapToUpdatedUser(TherapistProfileUpdateRequest therapistRequest, User userOnFile)
    {
        if (!string.IsNullOrEmpty(therapistRequest.FirstName)) { userOnFile.FirstName = therapistRequest.FirstName; }
        if (!string.IsNullOrEmpty(therapistRequest.MiddleName)) { userOnFile.MiddleName = therapistRequest.MiddleName; }
        if (!string.IsNullOrEmpty(therapistRequest.LastName)) { userOnFile.LastName = therapistRequest.LastName; }
        if (!string.IsNullOrEmpty(therapistRequest.Email)) { userOnFile.Email = therapistRequest.Email; }
        if (!string.IsNullOrEmpty(therapistRequest.PhoneNumber)) { userOnFile.PhoneNumber = therapistRequest.PhoneNumber; }
        if (userOnFile.ActiveStatus != therapistRequest.ActiveStatus) { userOnFile.ActiveStatus = therapistRequest.ActiveStatus; }

        return userOnFile;
    }

    private static TherapistProfile ExtractTherapistProfile(Therapist t)
    {
        if (t.User == null)
        {
            throw new ArgumentException(nameof(t.User) + " must not be null");
        }

        return new TherapistProfile
        {
            TherapistId = t.TherapistId,
            UserId = t.User.Id,
            TherapistName = $"{t.User.LastName}, {t.User.FirstName} {t.User.MiddleName}".Trim(),
            Email = t.User.Email,
            PhoneNumber = t.User.PhoneNumber,
            CreatedTimestamp = t.User.CreatedTimestamp,
            IsActive = t.User.ActiveStatus,
            FeePerSession = t.FeePerSession,
            FeePctPerSession = t.FeePctPerSession
        };
    }
}
