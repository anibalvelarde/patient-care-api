using Neurocorp.Api.Core.BusinessObjects.Therapists;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace Neurocorp.Api.Core.Services;

public class TherapistProfileService : ITherapistProfileService
{
    private readonly ILogger<TherapistProfileService> _logger;
    private readonly ITherapistProfileRepository _repository;
    private readonly ITherapistRepository _therapistRepo;
    private readonly IUserRepository _userRepo;
    private readonly IUserRoleRepository _userRoleRepo;

    public TherapistProfileService(ILogger<TherapistProfileService> logger, ITherapistProfileRepository profileRepository, ITherapistRepository therapistRepository, IUserRepository userRepo, IUserRoleRepository userRoleRepo)
    {
        _logger = logger;
        _repository = profileRepository;
        _therapistRepo = therapistRepository;
        _userRepo = userRepo;
        _userRoleRepo = userRoleRepo;
    }

    public async Task<IEnumerable<TherapistProfile>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<TherapistProfile?> GetByIdAsync(int id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task<TherapistProfile> CreateAsync(TherapistProfile patient)
    {
        return await Task.FromException<TherapistProfile>(new NotImplementedException());
    }

    public async Task UpdateAsync(TherapistProfile patient)
    {
        await Task.FromException<TherapistProfile>(new NotImplementedException());
    }

    public async Task<TherapistProfile> CreateAsync(TherapistProfileRequest therapistRequest)
    {
        _logger.LogInformation("Started a new Therapist Profile request.");
        var newUser = await _userRepo.AddAsync(MapToNewUser(therapistRequest));
        var newTherapist = await _therapistRepo.AddAsync(MapToNewTherapist(therapistRequest, newUser));
        var newRole = await _userRoleRepo.AddAsync(newTherapist.MintNewRole());
        _logger.LogInformation($"New Patient Profile was created: Uid[{newUser.Id}], Tid[{newTherapist.TherapistId}], Role[{newRole.UserRoleId}]");
        return new TherapistProfile
        {
            TherapistId = newTherapist.TherapistId,
            UserId = newUser.Id,
            TherapistName = $"{newUser.LastName}, {newUser.FirstName} {newUser.MiddleName}".Trim(),
            Email = newUser.Email,
            PhoneNumber = newUser.PhoneNumber,
            CreatedTimestamp = newUser.CreatedTimestamp,
            FeePerSession = newTherapist.FeePerSession,
            FeePctPerSession = newTherapist.FeePctPerSession,
        };
    }

    public async Task<bool> UpdateAsync(int therapistAggId, TherapistProfileUpdateRequest updateRequest)
    {
        ArgumentNullException.ThrowIfNull(updateRequest);

        // ensure the Patient Profile exists...
        var profileOnFile = await this.GetByIdAsync(therapistAggId);
        if (profileOnFile != null)
        {
            await _repository.UpdateAsync(profileOnFile.TherapistId, profileOnFile.UserId, updateRequest);
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

    public async Task<bool> VerifyRequestAsync(int therapistAggId, TherapistProfileUpdateRequest request)
    {
        var profile = await this.GetByIdAsync(therapistAggId);
        if (profile != null)
        {
            return profile.TherapistId.Equals(therapistAggId);
        }
        return false;
    }

    private static Therapist MapToNewTherapist(TherapistProfileRequest therapistRequest, User user)
    {
        return new Therapist
        {
            UserId = user.Id,
            FeePerSession = therapistRequest.FeePerSession,
            FeePctPerSession = therapistRequest.FeePctPerSession
        };
    }

    private static User MapToNewUser(TherapistProfileRequest therapistRequest)
    {
        return new User
        {
            FirstName = therapistRequest.FirstName,
            MiddleName = therapistRequest.MiddleName,
            LastName = therapistRequest.LastName,
            Email = therapistRequest.Email,
            PhoneNumber = therapistRequest.PhoneNumber,
            CreatedTimestamp = DateTime.UtcNow,
            ActiveStatus = true
        };
    }
}
