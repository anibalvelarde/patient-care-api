using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.BusinessObjects;
using System.Linq;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class PatientProfileRepository(ApplicationDbContext dbContext) :
    EfRepository<PatientProfile>(dbContext), IPatientProfileRepository
{

    // Additional methods specific to Patient can be implemented here
    public override async Task<IReadOnlyList<PatientProfile>> GetAllAsync()
    {
        var result = await _dbContext.Patients
            .Where(p => p.User != null)
            .Include(p => p.User)
            .Select(p => ExtractPatientProfile(p)).ToListAsync();

        return result;
    }

    public override async Task<PatientProfile?> GetByIdAsync(int id)
    {
        var result = await _dbContext.Patients
        .Where(p => p.PatientId == id)
        .Include(p => p.User)
        .Select(p => ExtractPatientProfile(p))
        .ToListAsync();

        return result.FirstOrDefault();
    }

    public override async Task<PatientProfile> AddAsync(PatientProfile entity)
    {
        return await Task.FromException<PatientProfile>(new NotImplementedException());
    }

    private static PatientProfile ExtractPatientProfile(Patient p)
    {
        if (p.User == null)
        {
            throw new ArgumentException(nameof(p.User) + " must not be null");
        }
        return new PatientProfile
        {
            PatientId = p.PatientId,
            UserId = p.User.UserId,
            PatientName = $"{p.User.LastName}, {p.User.FirstName} {p.User.MiddleName}".Trim(),
            MedicalRecordNumber = p.MedicalRecordNumber,
            DateOfBirth = p.DateOfBirth ?? DateTime.MinValue,
            Email = p.User.Email,
            PhoneNumber = p.User.PhoneNumber,
            CreatedTimestamp = p.User.CreatedTimestamp
        };
    }
}
