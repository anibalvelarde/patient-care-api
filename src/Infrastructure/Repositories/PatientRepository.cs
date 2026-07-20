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
}
