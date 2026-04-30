using Neurocorp.Api.Core.BusinessObjects.Statements;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface ITherapistStatementService
{
    Task<TherapistStatement?> GetStatementAsync(int therapistId, DateOnly? from, DateOnly? to);
}
