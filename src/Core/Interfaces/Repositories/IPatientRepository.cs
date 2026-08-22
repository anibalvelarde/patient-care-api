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

    /// <summary>
    /// WP-50B: the patient with its <see cref="Neurocorp.Api.Core.Entities.User"/> navigation
    /// eagerly loaded and TRACKED. The base <c>GetByIdAsync</c> uses <c>FindAsync</c> and does not
    /// load navigations; the self-caretaker flow needs the patient's existing SystemUser so it can
    /// attach a Caretaker role to it rather than mint a new user.
    /// </summary>
    Task<Patient?> GetByIdWithUserAsync(int id);
}
