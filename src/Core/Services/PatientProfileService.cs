using Neurocorp.Api.Core.BusinessObjects;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Neurocorp.Api.Core.Services;

public class PatientProfileService : IPatientProfileService
{
    private readonly IPatientProfileRepository _repository;
    private readonly IPatientRepository _patientRepo;
    private readonly IUserRepository _userRepo;

    public PatientProfileService(IPatientProfileRepository patientProfileRepository, IPatientRepository patientRepository, IUserRepository userRepo)
    {
        _repository = patientProfileRepository;
        _patientRepo = patientRepository;
        _userRepo = userRepo;
    }

    public async Task<IEnumerable<PatientProfile>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<PatientProfile?> GetByIdAsync(int id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task<PatientProfile> CreateAsync(PatientProfile patient)
    {
        return await Task.FromException<PatientProfile>(new NotImplementedException());
    }

    public async Task UpdateAsync(PatientProfile patient)
    {
        await Task.FromException<PatientProfile>(new NotImplementedException());
    }

    public async Task<PatientProfile> CreateAsync(PatientProfileRequest patientRequest)
    {
        var newUser = await _userRepo.AddAsync(MapToNewUser(patientRequest));
        var newPatient = await _patientRepo.AddAsync(MapToNewPatient(patientRequest, newUser));
        return new PatientProfile
        {
            PatientId = newPatient.PatientId,
            UserId = newUser.UserId,
            PatientName = $"{newUser.LastName}, {newUser.FirstName} {newUser.MiddleName}".Trim(),
            DateOfBirth = newPatient.DateOfBirth ?? DateTime.MinValue,
            Email = newUser.Email,
            PhoneNumber = newUser.PhoneNumber,
            CreatedTimestamp = newUser.CreatedTimestamp,
            IsActive = newUser.ActiveStatus
        };
    }

    public async Task<bool> UpdateAsync(int patientAggId, PatientProfileUpdateRequest updateRequest)
    {
        ArgumentNullException.ThrowIfNull(updateRequest);

        // ensure the Patient Profile exists...
        var profileOnFile = await this.GetByIdAsync(patientAggId);
        if (profileOnFile != null)
        {
            await _repository.UpdateAsync(profileOnFile.PatientId, profileOnFile.UserId, updateRequest);
            return true;
        }
        return false;
    }

    public async Task DeleteAsync(int id)
    {
        var profile = await _repository.GetByIdAsync(id);
        if (profile != null)
        {
            await _repository.DeleteAsync(profile);
        }
    }

    public async Task<bool> VerifyRequestAsync(int patientAggId, PatientProfileUpdateRequest request)
    {
        var profile = await this.GetByIdAsync(patientAggId);
        if (profile != null)
        {
            return profile.PatientId.Equals(patientAggId);
        }
        return false;
    }

    private static Patient MapToNewPatient(PatientProfileRequest patientRequest, User user)
    {
        return new Patient
        {
            UserId = user.UserId,
            DateOfBirth = patientRequest.DateOfBirth,
            Gender = patientRequest.Gender,
            MedicalRecordNumber = patientRequest.MedicalRecordNumber
        };
    }

    private static User MapToNewUser(PatientProfileRequest patientRequest)
    {
        return new User
        {
            FirstName = patientRequest.FirstName,
            MiddleName = patientRequest.MiddleName,
            LastName = patientRequest.LastName,
            Email = patientRequest.Email,
            PhoneNumber = patientRequest.PhoneNumber,
            CreatedTimestamp = DateTime.UtcNow,
            ActiveStatus = true
        };
    }
}
