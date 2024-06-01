using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.BusinessObjects;
using System.Linq;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class CaretakerProfileRepository(ApplicationDbContext dbContext) :
    EfRepository<CaretakerProfile>(dbContext), ICaretakerProfileRepository
{

    // Additional methods specific to Patient can be implemented here
    public override async Task<IReadOnlyList<CaretakerProfile>> GetAllAsync()
    {
        var result = await _dbContext.Caretakers
            .Where(p => p.User != null)
            .Include(p => p.User)
            .Select(p => ExtractCaretakerProfile(p)).ToListAsync();

        return result;
    }

    public override async Task<CaretakerProfile?> GetByIdAsync(int id)
    {
        var result = await _dbContext.Caretakers
        .Where(p => p.Id == id)
        .Include(p => p.User)
        .Select(p => ExtractCaretakerProfile(p))
        .ToListAsync();
        return result.FirstOrDefault();
    }

    public override async Task<CaretakerProfile> AddAsync(CaretakerProfile entity)
    {
        return await Task.FromException<CaretakerProfile>(new NotImplementedException());
    }

    public override async Task<CaretakerProfile> UpdateAsync(CaretakerProfile entity)
    {
        return await Task.FromException<CaretakerProfile>(new NotImplementedException());
    }

    public async Task<CaretakerProfile> UpdateAsync(int caretakerId, int userId, CaretakerProfileUpdateRequest updateRequest)
    {
        // fetch the entities & ensure they are valid...
        var caretaker = await _dbContext.Caretakers
            .Include(p => p.User)
            .FirstAsync(p => p.Id == caretakerId);

        // update entity props & save changes.
        caretaker = MapToUpdatedCaretaker(updateRequest, caretaker);
        caretaker.User = MapToUpdatedUser(updateRequest, caretaker.User ?? new User());
        _dbContext.ChangeTracker.DetectChanges();
        await _dbContext.SaveChangesAsync();

        return ExtractCaretakerProfile(caretaker);
    }


    private static Caretaker MapToUpdatedCaretaker(CaretakerProfileUpdateRequest request, Caretaker caretakerOnFile)
    {
        if(!string.IsNullOrEmpty(request.Notes)
            && !string.IsNullOrEmpty(request.Notes)
            && !request.Notes.Equals(caretakerOnFile.Notes)
        )
        {
            caretakerOnFile.Notes = request.Notes;
        }
        return caretakerOnFile;
    }

    private static User MapToUpdatedUser(CaretakerProfileUpdateRequest request, User userOnFile)
    {
        if (!string.IsNullOrEmpty(request.FirstName)) { userOnFile.FirstName = request.FirstName; }
        if (!string.IsNullOrEmpty(request.MiddleName)) { userOnFile.MiddleName = request.MiddleName; }
        if (!string.IsNullOrEmpty(request.LastName)) { userOnFile.LastName = request.LastName; }
        if (!string.IsNullOrEmpty(request.Email)) { userOnFile.Email = request.Email; }
        if (!string.IsNullOrEmpty(request.PhoneNumber)) { userOnFile.PhoneNumber = request.PhoneNumber; }
        if (!userOnFile.ActiveStatus && userOnFile.ActiveStatus != request.IsActive) { userOnFile.ActiveStatus = request.IsActive; }

        return userOnFile;
    }

    private static CaretakerProfile ExtractCaretakerProfile(Caretaker ct)
    {
        if (ct.User == null)
        {
            throw new ArgumentException(nameof(ct.User) + " must not be null");
        }

        return new CaretakerProfile
        {
            CaretakerId = ct.Id,
            UserId = ct.User.Id,
            IsActive = ct.User.ActiveStatus,
            CaretakerName = $"{ct.User.LastName}, {ct.User.FirstName} {ct.User.MiddleName}".Trim(),
            Notes = ct.Notes ?? string.Empty,
            Email = ct.User.Email,
            PhoneNumber = ct.User.PhoneNumber,
            CreatedTimestamp = ct.User.CreatedTimestamp,
            LastUpdated = ct.User.LastUpdatedTimestamp
        };
    }
}
