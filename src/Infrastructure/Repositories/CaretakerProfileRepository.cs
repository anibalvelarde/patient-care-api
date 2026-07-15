using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.BusinessObjects.Common;
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
            .Include(p => p.Patients)
                .ThenInclude(pc => pc.Patient)
                    .ThenInclude(p => p!.User)
            .Select(p => ExtractCaretakerProfile(p)).ToListAsync();

        return result;
    }

    public override async Task<CaretakerProfile?> GetByIdAsync(int id)
    {
        var result = await _dbContext.Caretakers
        .Where(p => p.Id == id)
        .Include(p => p.User)
        .Include(p => p.Patients)
            .ThenInclude(pc => pc.Patient)
                .ThenInclude(p => p!.User)
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

    // WP-30 (U2): paged main list. Count and page-id selection run include-free (the Patients
    // include join-amplifies); only the page hydrates through includes, then reorders.
    public async Task<PagedResult<CaretakerProfile>> GetPagedAsync(string? search, bool? isActive, int page, int pageSize)
    {
        var filtered = ApplyListFilters(search, isActive);
        var totalCount = await filtered.CountAsync();

        var pageIds = await filtered
            .OrderBy(c => c.User!.LastName)
            .ThenBy(c => c.User!.FirstName)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => c.Id)
            .ToListAsync();

        var profiles = await _dbContext.Caretakers
            .Where(c => pageIds.Contains(c.Id))
            .Include(c => c.User)
            .Include(c => c.Patients)
                .ThenInclude(pc => pc.Patient)
                    .ThenInclude(p => p!.User)
            .Select(c => ExtractCaretakerProfile(c))
            .ToListAsync();
        var profilesById = profiles.ToDictionary(c => c.CaretakerId);
        var items = pageIds.Where(profilesById.ContainsKey).Select(id => profilesById[id]).ToList();

        return new PagedResult<CaretakerProfile>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    // WP-30 (U2): typeahead — slim include-free projection, capped by the caller (gate G1: 20).
    public async Task<IReadOnlyList<CaretakerLookupItem>> LookupAsync(string query, int maxResults)
    {
        var rows = await ApplyListFilters(query, isActive: null)
            .OrderBy(c => c.User!.LastName)
            .ThenBy(c => c.User!.FirstName)
            .ThenBy(c => c.Id)
            .Take(maxResults)
            .Select(c => new
            {
                c.Id,
                c.User!.FirstName,
                c.User!.LastName,
                c.User!.MiddleName,
            })
            .ToListAsync();

        return rows.Select(r => new CaretakerLookupItem
        {
            CaretakerId = r.Id,
            CaretakerName = $"{r.LastName}, {r.FirstName} {r.MiddleName}".Trim(),
        }).ToList();
    }

    // Search fields per gate G2: name + email. Explicit ToLower() keeps the InMemory test
    // provider aligned with MySQL's _ci collation (WP-21 pattern).
    private IQueryable<Caretaker> ApplyListFilters(string? search, bool? isActive)
    {
        var caretakers = _dbContext.Caretakers.Where(c => c.User != null);
        if (isActive.HasValue)
        {
            caretakers = caretakers.Where(c => c.User!.ActiveStatus == isActive.Value);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            caretakers = caretakers.Where(c =>
                c.User!.FirstName.ToLower().Contains(term) ||
                c.User!.LastName.ToLower().Contains(term) ||
                (c.User!.Email != null && c.User!.Email.ToLower().Contains(term)));
        }
        return caretakers;
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
        if (userOnFile.ActiveStatus != request.IsActive) { userOnFile.ActiveStatus = request.IsActive; }

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
            LastUpdated = ct.User.LastUpdatedTimestamp,
            Patients = ct.Patients.Select(pc => new CaretakerPatientSummary
            {
                PatientId = pc.PatientId,
                PatientName = pc.Patient?.User != null
                    ? $"{pc.Patient.User.LastName}, {pc.Patient.User.FirstName} {pc.Patient.User.MiddleName}".Trim()
                    : string.Empty,
                IsPrimaryCaretaker = pc.PrimaryCaretaker,
                RelationshipToPatient = pc.RelationshipToPatient
            }).ToList()
        };
    }
}
