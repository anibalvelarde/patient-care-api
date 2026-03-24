using Neurocorp.Api.Core.BusinessObjects.Statements;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface IAccountStatementService
{
    Task<AccountStatement?> GetStatementAsync(int caretakerId, DateOnly? from, DateOnly? to);
}
