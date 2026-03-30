using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.BusinessObjects.Sessions;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface IAppointmentStatusService
{
    Task<IEnumerable<AppointmentStatusInfo>> GetAllAsync();
    Task<AppointmentStatusInfo?> GetByIdAsync(int id);
    Task<AppointmentStatusInfo> CreateAsync(AppointmentStatusCreateRequest request);
    Task<bool> UpdateAsync(int id, AppointmentStatusUpdateRequest request);
}
