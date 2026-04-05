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
    private readonly ITherapistSpecialtyRepository _specialtyRepo;

    public TherapistProfileService(ILogger<TherapistProfileService> logger, ITherapistProfileRepository profileRepository, ITherapistRepository therapistRepository, IUserRepository userRepo, IUserRoleRepository userRoleRepo, ITherapistSpecialtyRepository specialtyRepo)
    {
        _logger = logger;
        _repository = profileRepository;
        _therapistRepo = therapistRepository;
        _userRepo = userRepo;
        _userRoleRepo = userRoleRepo;
        _specialtyRepo = specialtyRepo;
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

        var invalidIds = await GetInvalidSpecialtyIdsAsync(therapistRequest.SpecialtyIds);
        if (invalidIds.Any())
            throw new ArgumentException($"Invalid specialty IDs: {string.Join(", ", invalidIds)}");

        var newUser = await _userRepo.AddAsync(MapToNewUser(therapistRequest));
        var newTherapist = await _therapistRepo.AddAsync(MapToNewTherapist(therapistRequest, newUser));
        var newRole = await _userRoleRepo.AddAsync(newTherapist.MintNewRole());
        await _specialtyRepo.SetSpecialtiesAsync(newTherapist.Id, therapistRequest.SpecialtyIds);
        _logger.LogInformation($"New Therapist Profile was created: Uid[{newUser.Id}], Tid[{newTherapist.Id}], Role[{newRole.UserRoleId}]");

        var profile = await _repository.GetByIdAsync(newTherapist.Id);
        return profile!;
    }

    public async Task<bool> UpdateAsync(int therapistAggId, TherapistProfileUpdateRequest updateRequest)
    {
        ArgumentNullException.ThrowIfNull(updateRequest);

        // ensure the Patient Profile exists...
        var profileOnFile = await this.GetByIdAsync(therapistAggId);
        if (profileOnFile != null)
        {
            if (updateRequest.SpecialtyIds != null)
            {
                var invalidIds = await GetInvalidSpecialtyIdsAsync(updateRequest.SpecialtyIds);
                if (invalidIds.Any())
                    throw new ArgumentException($"Invalid specialty IDs: {string.Join(", ", invalidIds)}");

                await _specialtyRepo.SetSpecialtiesAsync(profileOnFile.TherapistId, updateRequest.SpecialtyIds);
            }

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

    public async Task<bool> VerifyRequestAsync(int therapistAggId)
    {
        var profile = await this.GetByIdAsync(therapistAggId);
        if (profile != null)
        {
            return true;
        }
        return false;
    }

    private async Task<List<int>> GetInvalidSpecialtyIdsAsync(IEnumerable<int> specialtyIds)
    {
        var ids = specialtyIds.Distinct().ToList();
        var validIds = await _specialtyRepo.GetValidSpecialtyIdsAsync(ids);
        var validSet = validIds.ToHashSet();
        return ids.Where(id => !validSet.Contains(id)).ToList();
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
