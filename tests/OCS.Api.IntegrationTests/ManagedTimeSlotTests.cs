using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Controllers;
using OCS.Api.Entities;
using OCS.Api.Models.Appointments;
using OCS.Api.Repositories;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class ManagedTimeSlotTests
{
    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task CreateRejectsInvalidAndOverlappingHours()
    {
        await using var db = CreateContext();
        var service = new ManagedTimeSlotService(db);
        var slot = new SaveManagedTimeSlotRequest(new TimeOnly(8, 0), new TimeOnly(9, 0), 5, true, null);
        Assert.True((await service.CreateAsync(slot, default)).IsSuccess);
        Assert.Equal("TIME_SLOT.OVERLAP", (await service.CreateAsync(slot with { StartTime = new TimeOnly(8, 30), EndTime = new TimeOnly(9, 30) }, default)).Error!.Code);
        Assert.Equal("TIME_SLOT.INVALID", (await service.CreateAsync(slot with { EndTime = new TimeOnly(7, 0) }, default)).Error!.Code);
        Assert.True((await service.CreateAsync(slot with { StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0) }, default)).IsSuccess);
    }

    [Fact]
    public async Task ExistingAppointmentsProtectTimeAndCapacityButAllowDisabling()
    {
        await using var db = CreateContext();
        var slot = new AppointmentTimeSlot { StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(9, 0), MaxCustomers = 3 };
        var account = new Account { FullName = "Khách", PhoneNumber = "0900000000" };
        var customer = new CustomerProfile { Id = account.Id, Account = account };
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(1);
        db.Appointments.AddRange(Enumerable.Range(0, 2).Select(_ => new Appointment { Customer = customer, TimeSlot = slot, AppointmentDate = tomorrow, EstimatedItemCount = 1, Status = AppointmentStatus.Scheduled }));
        await db.SaveChangesAsync();
        var service = new ManagedTimeSlotService(db);
        var request = new SaveManagedTimeSlotRequest(slot.StartTime, slot.EndTime, 3, false, slot.UpdatedAt);
        Assert.Equal("TIME_SLOT.HAS_APPOINTMENTS", (await service.UpdateAsync(slot.Id, request with { EndTime = new TimeOnly(10, 0) }, default)).Error!.Code);
        Assert.Equal("TIME_SLOT.CAPACITY", (await service.UpdateAsync(slot.Id, request with { MaxCustomers = 1 }, default)).Error!.Code);
        Assert.True((await service.UpdateAsync(slot.Id, request, default)).IsSuccess);
        Assert.False(slot.IsActive);
        Assert.Equal(2, await db.Appointments.CountAsync());
        Assert.Equal("TIME_SLOT.CHANGED", (await service.UpdateAsync(slot.Id, request, default)).Error!.Code);
    }

    [Fact]
    public void ControllerAllowsOnlyManagerAndAdmin() =>
        Assert.Equal("Admin,Manager", typeof(ManagedTimeSlotsController).GetCustomAttribute<AuthorizeAttribute>()!.Roles);

    [PostgresFact]
    public async Task ConcurrentBookingsCannotExceedCapacityAcrossContexts()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var setup = database.Context();
        var slot = new AppointmentTimeSlot { StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(9, 0), MaxCustomers = 1 };
        var accounts = new[] { "0900000001", "0900000002" }.Select(phone => new Account { FullName = "Khách", PhoneNumber = phone, AccountType = AccountType.Customer }).ToArray();
        setup.AppointmentTimeSlots.Add(slot);
        setup.CustomerProfiles.AddRange(accounts.Select(account => new CustomerProfile { Id = account.Id, Account = account }));
        await setup.SaveChangesAsync();
        await using var first = database.Context();
        await using var second = database.Context();
        static UnitOfWork Unit(AppDbContext context) => new(context, new ProductRepository(context), new AuthRepository(context), new AppointmentRepository(context), new NotificationRepository(context), new CustomerProfileRepository(context));
        using var firstUnit = Unit(first);
        using var secondUnit = Unit(second);
        var request = new CreateAppointmentRequest { AppointmentDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(1), TimeSlotId = slot.Id, EstimatedItemCount = 1 };
        var results = await Task.WhenAll(new AppointmentService(firstUnit, new NotificationService(firstUnit)).CreateAsync(accounts[0].Id, request),
            new AppointmentService(secondUnit, new NotificationService(secondUnit)).CreateAsync(accounts[1].Id, request));
        Assert.Single(results, result => result.IsSuccess);
        Assert.Single(results, result => result.Error?.Code == "SLOT_FULL");
    }
}
