using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Interfaces.Repositories;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Neurocorp.Api.Core.Services;

public class PatientProfileService : IPatientProfileService
{
    private readonly IPatientProfileRepository _repository;
    private readonly IPatientRepository _patientRepo;
    private readonly IUserRepository _userRepo;
    private readonly IUserRoleRepository _userRoleRepo;
    private readonly ILogger<PatientProfileService> _logger;

    public PatientProfileService(
        ILogger<PatientProfileService> logger,
        IPatientProfileRepository patientProfileRepository,
        IPatientRepository patientRepository,
        IUserRepository userRepo,
        IUserRoleRepository userRoleRepo)
    {
        _repository = patientProfileRepository;
        _patientRepo = patientRepository;
        _userRepo = userRepo;
        _userRoleRepo = userRoleRepo;
        _logger = logger;
    }

    public async Task<IEnumerable<PatientProfile>> GetAllAsync()
    {
        _logger.LogInformation("Getting all patient profiles.");
        return await _repository.GetAllAsync();
    }

    public async Task<PatientProfile?> GetByIdAsync(int id)
    {
        _logger.LogInformation("Getting patient profile by ID: {Id}", id);
        return await _repository.GetByIdAsync(id);
    }

    public async Task<PatientProfile> CreateAsync(PatientProfile patient)
    {
        _logger.LogInformation("Creating new patient profile.");
        return await Task.FromException<PatientProfile>(new NotImplementedException());
    }

    public async Task UpdateAsync(PatientProfile patient)
    {
        _logger.LogInformation("Updating patient profile.");
        await Task.FromException<PatientProfile>(new NotImplementedException());
    }

    public async Task<PatientProfile> CreateAsync(PatientProfileRequest patientRequest)
    {
        _logger.LogInformation("Creating new patient profile from request.");
        var newUser = await _userRepo.AddAsync(MapToNewUser(patientRequest));
        var newPatient = await _patientRepo.AddAsync(MapToNewPatient(patientRequest, newUser));
        var newRole = await _userRoleRepo.AddAsync(newPatient.MintNewRole());
        _logger.LogInformation($"New Patient Profile was created: Uid[{newUser.Id}], Pid[{newPatient.Id}], Role[{newRole.UserRoleId}]");
        return new PatientProfile
        {
            PatientId = newPatient.Id,
            UserId = newUser.Id,
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
        _logger.LogInformation("Updating patient profile with ID: {Id}", patientAggId);
        ArgumentNullException.ThrowIfNull(updateRequest);

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
        _logger.LogInformation("Deleting patient profile with ID: {Id}", id);
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
            var verificationResult = profile.PatientId.Equals(patientAggId);
            var passOrFailed = verificationResult ? "PASS" : "FAIL";
            _logger.LogInformation($"Request for patient profile ID: {patientAggId}  Result: {passOrFailed}");
            return verificationResult;
        }
        return false;
    }

    private static Patient MapToNewPatient(PatientProfileRequest patientRequest, User user)
    {
        return new Patient
        {
            User = user,
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
