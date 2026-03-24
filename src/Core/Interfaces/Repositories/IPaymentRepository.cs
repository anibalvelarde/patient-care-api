using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface IPaymentRepository : IRepository<Payment>
{
    Task<IReadOnlyList<Payment>> GetByCaretakerIdAsync(int caretakerId);
    Task<Payment?> GetByIdWithDetailsAsync(int paymentId);
    Task<IReadOnlyList<Payment>> GetAllWithDetailsAsync();
    Task<IReadOnlyList<Payment>> GetByCaretakerIdAndDateRangeAsync(int caretakerId, DateTime from, DateTime to);
}