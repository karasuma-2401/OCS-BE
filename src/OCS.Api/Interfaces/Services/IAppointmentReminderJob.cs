namespace OCS.Api.Interfaces.Services;

public interface IAppointmentReminderJob
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}

