using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.BusinessObjects;

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
            .Select(p => new PatientProfile
            {
                PatientId = p.PatientId,
                UserId = p.User.UserId,
                PatientName = $"{p.User.LastName}, {p.User.FirstName} {p.User.MiddleName}".Trim(),
                MedicalRecordNumber = p.MedicalRecordNumber,
                DateOfBirth = p.DateOfBirth ?? DateTime.MinValue,
                Email = p.User.Email,
                PhoneNumber = p.User.PhoneNumber,
                CreatedTimestamp = p.User.CreatedTimestamp
            }).ToListAsync();

        return result;
    }
}
