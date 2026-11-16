using Microsoft.EntityFrameworkCore;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Repositories;

namespace OCS.Api.IntegrationTests;

public sealed class AppointmentDateFilterTests
{
    [Fact]
    public async Task UpcomingFilterIncludesTodayAndFutureAndExactDateStillWorks()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var slot = new AppointmentTimeSlot { StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(9, 0), MaxCustomers = 5 };
        var today = new DateOnly(2026, 11, 13);
        var account = new Account { FullName = "Khách thử", PhoneNumber = "0900000000" };
        var customer = new CustomerProfile { Id = account.Id, Account = account };
        db.CustomerProfiles.Add(customer);
        db.AppointmentTimeSlots.Add(slot);
        db.Appointments.AddRange(new[] { today.AddDays(-1), today, today.AddDays(1) }.Select(date =>
            new Appointment { AppointmentDate = date, TimeSlot = slot, Customer = customer, EstimatedItemCount = 1 }));
        await db.SaveChangesAsync();
        var repository = new AppointmentRepository(db);
        var upcoming = await repository.GetAppointmentsAsync(null, null, null, null, today);
        Assert.Equal(new[] { today, today.AddDays(1) }, upcoming.Select(item => item.AppointmentDate));
        var exact = await repository.GetAppointmentsAsync(today, null, null, null, null);
        Assert.Single(exact);
        Assert.Equal(today, exact[0].AppointmentDate);
    }
}
