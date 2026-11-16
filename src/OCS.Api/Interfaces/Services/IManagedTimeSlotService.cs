using OCS.Api.Common;
using OCS.Api.Models.Appointments;

namespace OCS.Api.Interfaces.Services;

public interface IManagedTimeSlotService
{
    Task<IReadOnlyList<ManagedTimeSlotResponse>> ListAsync(CancellationToken cancellationToken);
    Task<ManagedTimeSlotResponse?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<ManagedTimeSlotResponse>> CreateAsync(SaveManagedTimeSlotRequest request, CancellationToken cancellationToken);
    Task<Result<ManagedTimeSlotResponse>> UpdateAsync(Guid id, SaveManagedTimeSlotRequest request, CancellationToken cancellationToken);
}
