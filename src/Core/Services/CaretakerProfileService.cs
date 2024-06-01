
using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

public class CaretakerProfileService : ICaretakerProfileService
{
    private readonly ICaretakerProfileRepository _repository;
    private readonly IUserRepository _userRepo;
    private readonly ICaretakerRepository _caretakerRepo;
    private readonly IUserRoleRepository _userRoleRepo;
    private readonly ILogger<CaretakerProfileService> _logger;

    public CaretakerProfileService(
        ILogger<CaretakerProfileService> logger,
        ICaretakerProfileRepository repo,
        ICaretakerRepository caretakerRepo,
        IUserRepository userRepo,
        IUserRoleRepository userRoleRepo)
    {
        _logger = logger;
        _repository = repo;
        _userRepo = userRepo;
        _caretakerRepo = caretakerRepo;
        _userRoleRepo = userRoleRepo;
        _repository = repo;
    }

    public async Task<IEnumerable<CaretakerProfile>> GetAllAsync()
    {
        _logger.LogInformation("Getting all caretaker profiles");
        return await _repository.GetAllAsync();
    }

    public async Task<CaretakerProfile?> GetByIdAsync(int id)
    {
        _logger.LogInformation("Getting caretaker profile by ID: {id}", id);
        return await _repository.GetByIdAsync(id);
    }

    public async Task<CaretakerProfile> CreateAsync(CaretakerProfile caretaker)
    {
        _logger.LogError("Operation Not Allowed: Creating new patient profile.");
        return await Task.FromException<CaretakerProfile>(new NotImplementedException());
    }

    public async Task UpdateAsync(CaretakerProfile caretaker)
    {
        _logger.LogError("Updating patient profile.");
        await Task.FromException<CaretakerProfile>(new NotImplementedException());
    }

    public async Task<CaretakerProfile> CreateAsync(CaretakerProfileRequest request)
    {
        _logger.LogInformation("Creating new patient profile from request.");
        var newUser = await _userRepo.AddAsync(MapToNewUser(request));
        var newCaretaker = await _caretakerRepo.AddAsync(MapToNewCaretaker(request, newUser));
        var newRole = await _userRoleRepo.AddAsync(newCaretaker.MintNewRole());
        _logger.LogInformation($"New Patient Profile was created: Uid[{newUser.Id}], Pid[{newCaretaker.Id}], Role[{newRole.UserRoleId}]");
        return new CaretakerProfile
        {
            CaretakerId = newCaretaker.Id,
            UserId = newUser.Id,
            CaretakerName = $"{newUser.LastName}, {newUser.FirstName} {newUser.MiddleName}".Trim(),
            Email = newUser.Email,
            PhoneNumber = newUser.PhoneNumber,
            CreatedTimestamp = newUser.CreatedTimestamp,
            LastUpdated = MaxTimestamp(newUser.LastUpdatedTimestamp, newCaretaker.LastUpdatedTimestamp),
        };
    }

    public async Task<bool> UpdateAsync(int caretakerAggId, CaretakerProfileUpdateRequest updateRequest)
    {
        _logger.LogInformation("Updating patient profile with ID: {Id}", caretakerAggId);
        ArgumentNullException.ThrowIfNull(updateRequest);

        var profileOnFile = await this.GetByIdAsync(caretakerAggId);
        if (profileOnFile != null)
        {
            await _repository.UpdateAsync(profileOnFile.CaretakerId, profileOnFile.UserId, updateRequest);
            return true;
        }
        return false;
    }    

    public async Task DeleteAsync(int id)
    {
        _logger.LogInformation("Deleting caretaker profile with ID: {Id}", id);
        var profile = await _repository.GetByIdAsync(id);
        if (profile != null)
        {
            await _repository.DeleteAsync(profile);
        }
    }    

    public async Task<bool> VerifyRequestAsync(int caretakerAggId, CaretakerProfileUpdateRequest request)
    {
        var profile = await this.GetByIdAsync(caretakerAggId);
        if (profile != null)
        {
            var verificationResult = profile.CaretakerId.Equals(caretakerAggId);
            var passOrFailed = verificationResult ? "PASS" : "FAIL";
            _logger.LogInformation($"Request for patient profile ID: {caretakerAggId}  Result: {passOrFailed}");
            return verificationResult;
        }
        return false;
    }

    private static DateTime MaxTimestamp(DateTime timestamp1, DateTime timestamp2)
    {
        return new[] {timestamp1, timestamp2}.Max();
    }

    private static Caretaker MapToNewCaretaker(CaretakerProfileRequest request, User user)
    {
        return new Caretaker
        {
            User = user,
            Notes = request.Notes
        };
    }

    private static User MapToNewUser(CaretakerProfileRequest request)
    {
        return new User
        {
            FirstName = request.FirstName,
            MiddleName = request.MiddleName,
            LastName = request.LastName,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            CreatedTimestamp = DateTime.UtcNow,
            ActiveStatus = true
        };
    }

}