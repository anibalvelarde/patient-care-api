using Neurocorp.Api.Core.BusinessObjects.ServicePayments;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface IServicePaymentService
{
    Task<ServicePaymentRecord> CreateAsync(ServicePaymentRequest request);

    /// <summary>
    /// Reverse a service payment (WP-14.5, append-only). Writes one offsetting negative
    /// <see cref="Entities.ServicePayment"/> (negative <c>Amount</c> + negative allocations for every
    /// session the original covered) linked via <c>ReversesServicePaymentID</c>; the original is left
    /// untouched. The negative allocations re-open those sessions as unpaid. Throws
    /// <see cref="Exceptions.NotFoundException"/> if the original is missing, or
    /// <see cref="ArgumentException"/> if it is itself a reversal or has already been reversed.
    /// </summary>
    Task<ServicePaymentRecord> ReverseAsync(int servicePaymentId, ReverseServicePaymentRequest request);

    Task<IEnumerable<ServicePaymentRecord>> GetByTherapistAsync(int therapistId, DateOnly? from, DateOnly? to);
    Task<ServicePaymentRecord?> GetByIdAsync(int servicePaymentId);
    Task<IEnumerable<UnpaidProviderSessionSummary>> GetUnpaidProviderSessionsAsync(int therapistId, DateOnly? from, DateOnly? to);

    /// <summary>Per-therapist rollup of who is owed what in the window (drives the "Run Payroll" preview).</summary>
    Task<IEnumerable<PayrollPreviewTherapist>> GetPayrollPreviewAsync(DateOnly? from, DateOnly? to);

    /// <summary>
    /// Clinic-wide aggregate of what is still owed to therapists (drives the "Pending therapist
    /// payments" dashboard tile). Unlike the per-therapist views, this defaults to <b>all-time</b>
    /// when no range is given — it is an outstanding-liability figure, so older unpaid sessions must
    /// not be silently dropped.
    /// </summary>
    Task<PendingPayrollSummary> GetPendingPayrollSummaryAsync(DateOnly? from, DateOnly? to);

    /// <summary>
    /// Read-only per-therapist "Pending Pay" report — the decomposition behind the dashboard tile.
    /// Same population and all-time default as <see cref="GetPendingPayrollSummaryAsync"/>; adds
    /// date span, distinct-patient count, and gross-billed (pre-discount) per therapist.
    /// </summary>
    Task<PendingPayReport> GetPendingPayReportAsync(DateOnly? from, DateOnly? to);

    /// <summary>Issue one full-allocation ServicePayment per requested therapist that still has anything owed.</summary>
    Task<BatchPayrollResult> RunBatchPayrollAsync(BatchPayrollRequest request);

    /// <summary>
    /// Suggested quincena window for <paramref name="date"/> (1st–15th or 16th–EOM).
    /// A pure UX default — imposes no constraint on what range a payment may cover.
    /// </summary>
    QuincenaWindow GetQuincenaWindow(DateOnly date);
}
