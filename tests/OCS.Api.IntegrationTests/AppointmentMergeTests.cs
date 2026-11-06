using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Controllers;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Appointments;
using OCS.Api.Models.Notifications;
using OCS.Api.Repositories;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class AppointmentMergeTests
{
    [Theory]
    [InlineData(nameof(AppointmentsController.GetAppointments))]
    [InlineData(nameof(AppointmentsController.UpdateStatus))]
    [InlineData(nameof(AppointmentsController.StartReception))]
    public void StaffOperationsRequireStaffRoles(string action)
    {
        var method = typeof(AppointmentsController).GetMethod(action)!;
        var authorize = Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>(), attribute => attribute.Roles is not null);
        Assert.Equal("Admin,Manager,Staff", authorize.Roles);
    }

    [Fact]
    public void StatusRequestAcceptsFrontendStringEnum()
    {
        var request = JsonSerializer.Deserialize<UpdateAppointmentStatusRequest>("{\"Status\":\"CheckedIn\"}");
        Assert.NotNull(request);
        Assert.Equal(AppointmentStatus.CheckedIn, request.Status);
    }

    [PostgresFact]
    public async Task BookingAndNotificationCommitTogether()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Context();
        var account = new Account { Id = Guid.NewGuid(), FullName = "Test customer", PhoneNumber = "0901234567", AccountType = AccountType.Customer };
        context.CustomerProfiles.Add(new CustomerProfile { Id = account.Id, Account = account });
        var slot = new AppointmentTimeSlot { Id = Guid.NewGuid(), StartTime = new TimeOnly(0, 1), EndTime = new TimeOnly(0, 2), MaxCustomers = 3 };
        context.AppointmentTimeSlots.Add(slot);
        await context.SaveChangesAsync();

        using var unit = new UnitOfWork(context, new ProductRepository(context), new AuthRepository(context), new AppointmentRepository(context), new NotificationRepository(context), new CustomerProfileRepository(context));
        var request = new CreateAppointmentRequest { AppointmentDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(1), TimeSlotId = slot.Id, EstimatedItemCount = 1 };
        var service = new AppointmentService(unit, new NotificationService(unit));
        var result = await service.CreateAsync(account.Id, request);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, await context.Appointments.CountAsync());
        Assert.Equal(1, await context.Notifications.CountAsync());

        var failing = new AppointmentService(unit, new FailingNotificationService());
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.CreateAsync(account.Id, request));
        await using var verify = database.Context();
        Assert.Equal(1, await verify.Appointments.CountAsync());
        Assert.Equal(1, await verify.Notifications.CountAsync());
    }

    private sealed class FailingNotificationService : INotificationService
    {
        public Task<Result<NotificationDto>> CreateAsync(CreateNotificationDto dto, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated notification failure.");
        public Task<IReadOnlyList<NotificationDto>> GetForUserAsync(Guid accountId, int limit = 50, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> GetUnreadCountAsync(Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<bool>> MarkAsReadAsync(Guid notificationId, Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<bool>> MarkAllAsReadAsync(Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
