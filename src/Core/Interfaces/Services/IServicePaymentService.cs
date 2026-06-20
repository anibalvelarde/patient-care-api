using Neurocorp.Api.Core.BusinessObjects.ServicePayments;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface IServicePaymentService
{
    Task<ServicePaymentRecord> CreateAsync(ServicePaymentRequest request);
    Task<IEnumerable<ServicePaymentRecord>> GetByTherapistAsync(int therapistId, DateOnly? from, DateOnly? to);
    Task<ServicePaymentRecord?> GetByIdAsync(int servicePaymentId);
    Task<IEnumerable<UnpaidProviderSessionSummary>> GetUnpaidProviderSessionsAsync(int therapistId, DateOnly? from, DateOnly? to);

    /// <summary>
    /// Suggested quincena window for <paramref name="date"/> (1st–15th or 16th–EOM).
    /// A pure UX default — imposes no constraint on what range a payment may cover.
    /// </summary>
    QuincenaWindow GetQuincenaWindow(DateOnly date);
}
