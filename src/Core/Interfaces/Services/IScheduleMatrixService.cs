using Neurocorp.Api.Core.BusinessObjects.Sessions;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface IScheduleMatrixService
{
    Task<ScheduleMatrixResponse> GetMatrixAsync(DateOnly date, int siteId);
}
