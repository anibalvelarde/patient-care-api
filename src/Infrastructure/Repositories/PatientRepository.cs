using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class PatientRepository(ApplicationDbContext dbContext) :
    EfRepository<Patient>(dbContext), IPatientRepository
{
    // WP-36 (NP-1): MAX existing sequence under one year's mint prefix (e.g. "NC26-") — the
    // promote.py next_mrn_maker pattern, API-side. Slim scalar query in the WP-30 lookup style:
    // only the numeric suffixes travel (one year's worth of rows, no entity materialization).
    // The numeric MAX is taken client-side: a string MAX would break past 9999 ("10000" < "9999"
    // ordinally), and CAST-in-LINQ doesn't translate on the InMemory test provider. Non-numeric
    // suffixes (pre-WP-36 free-text MRNs) are ignored rather than faulting the mint.
    public async Task<int> GetMaxMrnSequenceAsync(string mrnPrefix)
    {
        var suffixes = await _dbContext.Patients
            .Where(p => p.MedicalRecordNumber != null && p.MedicalRecordNumber.StartsWith(mrnPrefix))
            .Select(p => p.MedicalRecordNumber!.Substring(mrnPrefix.Length))
            .ToListAsync();

        return suffixes
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
    }

    // WP-50B: load the patient WITH its SystemUser, tracked, so the self-caretaker flow can attach
    // a Caretaker role to the same user (assigning it as a navigation on the new Caretaker inserts
    // the FK without re-inserting the user). Base GetByIdAsync (FindAsync) loads no navigations.
    public async Task<Patient?> GetByIdWithUserAsync(int id)
    {
        return await _dbContext.Patients
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == id);
    }
}
