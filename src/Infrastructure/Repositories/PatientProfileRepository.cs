using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Interfaces.Repositories;
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
            .Include(p => p.Caretakers)
                .ThenInclude(pc => pc.Caretaker)
                    .ThenInclude(c => c!.User)
            .Select(p => ExtractPatientProfile(p)).ToListAsync();

        return result;
    }

    public override async Task<PatientProfile?> GetByIdAsync(int id)
    {
        var result = await _dbContext.Patients
        .Where(p => p.Id == id)
        .Include(p => p.User)
        .Include(p => p.Caretakers)
            .ThenInclude(pc => pc.Caretaker)
                .ThenInclude(c => c!.User)
        .Select(p => ExtractPatientProfile(p))
        .ToListAsync();
        return result.FirstOrDefault();
    }

    // WP-29 (U3): batched variant of GetByIdAsync — same includes/projection, one round trip.
    public async Task<IReadOnlyList<PatientProfile>> GetByIdsAsync(IReadOnlyCollection<int> patientIds)
    {
        if (patientIds.Count == 0) return [];

        return await _dbContext.Patients
            .Where(p => patientIds.Contains(p.Id))
            .Include(p => p.User)
            .Include(p => p.Caretakers)
                .ThenInclude(pc => pc.Caretaker)
                    .ThenInclude(c => c!.User)
            .Select(p => ExtractPatientProfile(p))
            .ToListAsync();
    }

    public override async Task<PatientProfile> AddAsync(PatientProfile entity)
    {
        return await Task.FromException<PatientProfile>(new NotImplementedException());
    }

    public override async Task<PatientProfile> UpdateAsync(PatientProfile entity)
    {
        return await Task.FromException<PatientProfile>(new NotImplementedException());
    }

    // WP-21 (F1): paged patient summaries for the Session History tab, ordered by
    // most-recent-session first. Recency/count come from correlated scalar subqueries — they
    // translate to SQL on every provider (incl. the InMemory test provider), and at this table's
    // scale (~1k patients / ~11k sessions) they're as good as a grouped join.
    public async Task<PagedResult<PatientSessionHistorySummary>> GetSessionHistoryAsync(string? search, int page, int pageSize)
    {
        var patients = _dbContext.Patients.Where(p => p.User != null);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Explicit ToLower(): MySQL columns are already _ci-collated; this keeps the
            // InMemory provider (LINQ-to-objects, case-sensitive Contains) behaving identically.
            var term = search.Trim().ToLower();
            patients = patients.Where(p =>
                p.User!.FirstName.ToLower().Contains(term) ||
                p.User!.LastName.ToLower().Contains(term) ||
                (p.MedicalRecordNumber != null && p.MedicalRecordNumber.ToLower().Contains(term)));
        }

        var totalCount = await patients.CountAsync();

        var rows = await patients
            .Select(p => new
            {
                p.Id,
                p.User!.FirstName,
                p.User!.LastName,
                p.User!.MiddleName,
                p.MedicalRecordNumber,
                LastSessionDate = _dbContext.TherapySessions
                    .Where(ts => ts.PatientId == p.Id)
                    .Max(ts => (DateOnly?)ts.SessionDate),
                TotalSessions = _dbContext.TherapySessions
                    .Count(ts => ts.PatientId == p.Id),
            })
            // MySQL puts NULLs last under ORDER BY ... DESC, matching .NET's comparer — patients
            // who never had a session deliberately sort to the end on both providers.
            .OrderByDescending(x => x.LastSessionDate)
            .ThenBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = rows.Select(r => new PatientSessionHistorySummary
        {
            PatientId = r.Id,
            PatientName = $"{r.LastName}, {r.FirstName} {r.MiddleName}".Trim(),
            MedicalRecordNumber = r.MedicalRecordNumber,
            LastSessionDate = r.LastSessionDate,
            TotalSessions = r.TotalSessions,
        }).ToList();

        return new PagedResult<PatientSessionHistorySummary>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    // WP-30 (U2): paged main list. Count and page-id selection run include-free (WP-21 lesson —
    // the Caretakers include join-amplifies); only the 30-row page hydrates through the batched
    // includes of GetByIdsAsync, then reorders to the page's name order.
    public async Task<PagedResult<PatientProfile>> GetPagedAsync(string? search, bool? isActive, int page, int pageSize)
    {
        var filtered = ApplyListFilters(search, isActive);
        var totalCount = await filtered.CountAsync();

        var pageIds = await filtered
            .OrderBy(p => p.User!.LastName)
            .ThenBy(p => p.User!.FirstName)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => p.Id)
            .ToListAsync();

        var profilesById = (await GetByIdsAsync(pageIds)).ToDictionary(p => p.PatientId);
        var items = pageIds.Where(profilesById.ContainsKey).Select(id => profilesById[id]).ToList();

        return new PagedResult<PatientProfile>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    // WP-30 (U2): typeahead — slim include-free projection, capped by the caller (gate G1: 20).
    public async Task<IReadOnlyList<PatientLookupItem>> LookupAsync(string query, int maxResults)
    {
        var rows = await ApplyListFilters(query, isActive: null)
            .OrderBy(p => p.User!.LastName)
            .ThenBy(p => p.User!.FirstName)
            .ThenBy(p => p.Id)
            .Take(maxResults)
            .Select(p => new
            {
                p.Id,
                p.User!.FirstName,
                p.User!.LastName,
                p.User!.MiddleName,
                p.MedicalRecordNumber,
            })
            .ToListAsync();

        return rows.Select(r => new PatientLookupItem
        {
            PatientId = r.Id,
            PatientName = $"{r.LastName}, {r.FirstName} {r.MiddleName}".Trim(),
            MedicalRecordNumber = r.MedicalRecordNumber,
        }).ToList();
    }

    // Search fields per gate G2: name + MRN + cedula. Explicit ToLower(): MySQL columns are
    // already _ci-collated; this keeps the InMemory provider behaving identically (WP-21 pattern).
    private IQueryable<Patient> ApplyListFilters(string? search, bool? isActive)
    {
        var patients = _dbContext.Patients.Where(p => p.User != null);
        if (isActive.HasValue)
        {
            patients = patients.Where(p => p.User!.ActiveStatus == isActive.Value);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            patients = patients.Where(p =>
                p.User!.FirstName.ToLower().Contains(term) ||
                p.User!.LastName.ToLower().Contains(term) ||
                (p.MedicalRecordNumber != null && p.MedicalRecordNumber.ToLower().Contains(term)) ||
                (p.Cedula != null && p.Cedula.ToLower().Contains(term)));
        }
        return patients;
    }

    public async Task<PatientProfile> UpdateAsync(int patientId, int userId, PatientProfileUpdateRequest updateRequest)
    {
        // fetch the entities & ensure they are valid...
        var patient = await _dbContext.Patients
            .Include(p => p.User)
            .FirstAsync(p => p.Id == patientId);

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
        if (!string.IsNullOrEmpty(patientRequest.MedicalRecordNumber))
        { patientOnFile.MedicalRecordNumber = patientRequest.MedicalRecordNumber; }
        // WP-25 (F5) — supersedes intake 2026-06-29-001 item 3 (blank-erase removed).
        // Omitted/null = leave as-is: this is the legacy-tolerance keystone that keeps the ~866
        // legacy-imported NULL-cedula patients editable (and the Active toggle working) — do NOT
        // touch it. Present-but-blank = rejected clear attempt (→ 400 via GlobalExceptionHandler);
        // non-blank = set (duplicate → 409 on uq_patient_cedula).
        if (patientRequest.Cedula != null)
        {
            if (string.IsNullOrWhiteSpace(patientRequest.Cedula))
            {
                throw new ArgumentException("Cedula | Passport cannot be cleared - it is required once on file.");
            }
            patientOnFile.Cedula = patientRequest.Cedula;
        }
        // WP-23 (F7): null = unchanged; the claim gate ran in the controller before we get here.
        if (patientRequest.HasSenadisDiscount.HasValue)
        {
            patientOnFile.HasSenadisDiscount = patientRequest.HasSenadisDiscount.Value;
        }
        // WP-24 (F3/F4): null = unchanged; the claim gate ran in the controller before we get here.
        if (patientRequest.RequiresDiscovery.HasValue)
        {
            patientOnFile.RequiresDiscovery = patientRequest.RequiresDiscovery.Value;
        }

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
            PatientId = p.Id,
            UserId = p.User.Id,
            IsActive = p.User.ActiveStatus,
            PatientName = $"{p.User.LastName}, {p.User.FirstName} {p.User.MiddleName}".Trim(),
            MedicalRecordNumber = p.MedicalRecordNumber,
            Cedula = p.Cedula,
            HasSenadisDiscount = p.HasSenadisDiscount,
            RequiresDiscovery = p.RequiresDiscovery,
            Gender = p.Gender,
            DateOfBirth = p.DateOfBirth ?? DateTime.MinValue,
            Email = p.User.Email,
            PhoneNumber = p.User.PhoneNumber,
            CreatedTimestamp = p.User.CreatedTimestamp,
            // WP-31 (U1): audit from the patient+user aggregate; updater name resolved in the service.
            Audit = AuditInfo.FromPersonAggregate(p, p.User),
            Caretakers = p.Caretakers.Select(pc => new PatientCaretakerSummary
            {
                CaretakerId = pc.CaretakerId,
                CaretakerName = pc.Caretaker?.User != null
                    ? $"{pc.Caretaker.User.LastName}, {pc.Caretaker.User.FirstName} {pc.Caretaker.User.MiddleName}".Trim()
                    : string.Empty,
                IsPrimaryCaretaker = pc.PrimaryCaretaker,
                RelationshipToPatient = pc.RelationshipToPatient
            }).ToList()
        };
    }
}
