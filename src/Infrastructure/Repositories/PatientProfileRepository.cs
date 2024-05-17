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

    public override async Task<PatientProfile> UpdateAsync(PatientProfile entity)
    {
        return await Task.FromException<PatientProfile>(new NotImplementedException());
    }

    public async Task<PatientProfile> UpdateAsync(int patientId, int userId, PatientProfileUpdateRequest updateRequest)
    {
        // fetch the entities & ensure they are valid...
        var patient = await _dbContext.Patients
            .Include(p => p.User)
            .FirstAsync(p => p.PatientId == patientId);

        // update entity props & save changes.
        patient = MapToUpdatedPatient(updateRequest, patient);
        patient.User = MapToUpdatedUser(updateRequest, patient.User ?? new User());
        _dbContext.ChangeTracker.DetectChanges();
        await _dbContext.SaveChangesAsync();

        return ExtractPatientProfile(patient);
    }


    private static Patient MapToUpdatedPatient(PatientProfileUpdateRequest patientRequest, Patient patientOnFile)
    {
        if (!string.IsNullOrEmpty(patientRequest.Gender)) { patientOnFile.Gender = patientRequest.Gender; }
        if (patientOnFile.DateOfBirth.HasValue
            && !patientRequest.DateOfBirth.Equals(DateTime.MinValue)
            && !patientOnFile.DateOfBirth.Value.Equals(patientRequest.DateOfBirth))
        { patientOnFile.DateOfBirth = patientRequest.DateOfBirth; }

        return patientOnFile;
    }

    private static User MapToUpdatedUser(PatientProfileUpdateRequest patientRequest, User userOnFile)
    {
        if (!string.IsNullOrEmpty(patientRequest.FirstName)) { userOnFile.FirstName = patientRequest.FirstName; }
        if (!string.IsNullOrEmpty(patientRequest.MiddleName)) { userOnFile.MiddleName = patientRequest.MiddleName; }
        if (!string.IsNullOrEmpty(patientRequest.LastName)) { userOnFile.LastName = patientRequest.LastName; }
        if (!string.IsNullOrEmpty(patientRequest.Email)) { userOnFile.Email = patientRequest.Email; }
        if (!string.IsNullOrEmpty(patientRequest.PhoneNumber)) { userOnFile.PhoneNumber = patientRequest.PhoneNumber; }
        if (userOnFile.ActiveStatus != patientRequest.ActiveStatus) { userOnFile.ActiveStatus = patientRequest.ActiveStatus; }

        return userOnFile;
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
            Gender = p.Gender,
            DateOfBirth = p.DateOfBirth ?? DateTime.MinValue,
            Email = p.User.Email,
            PhoneNumber = p.User.PhoneNumber,
            CreatedTimestamp = p.User.CreatedTimestamp
        };
    }
}
