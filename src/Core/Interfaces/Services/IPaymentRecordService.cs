using Neurocorp.Api.Core.BusinessObjects.Payments;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface IPaymentRecordService
{
    Task<IEnumerable<PaymentTypeInfo>> GetPaymentTypesAsync();
    Task<IEnumerable<PaymentRecord>> GetByCaretakerAsync(int caretakerId);
    Task<IEnumerable<PaymentRecord>> GetAllAsync();
    Task<PaymentRecord?> GetByIdAsync(int paymentId);
    Task<PaymentRecord> CreateAsync(PaymentRecordRequest request);
    Task UpdateAsync(int paymentId, PaymentRecordUpdateRequest request);
    Task<IEnumerable<UnpaidSessionSummary>> GetUnpaidSessionsForCaretakerAsync(int caretakerId);
    Task<IEnumerable<SessionPaymentDetail>> GetPaymentsForSessionAsync(int sessionId);
}
