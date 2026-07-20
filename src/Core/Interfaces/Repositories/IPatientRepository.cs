using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface IPatientRepository : IRepository<Patient>
{
    /// <summary>
    /// WP-36: highest numeric MRN sequence already minted under <paramref name="mrnPrefix"/>
    /// (e.g. "NC26-" → 42 when NC26-0042 is the top row), 0 when none exist. Slim scalar
    /// query — never materializes Patient entities.
    /// </summary>
    Task<int> GetMaxMrnSequenceAsync(string mrnPrefix);
}
