using System.Text.Json.Serialization;

namespace OCS.Api.Models.Appointments;

/// <summary>Khung giờ hẹn cùng số lượng tối đa mỗi ngày.</summary>
public sealed record ManagedTimeSlotResponse(Guid Id, TimeOnly StartTime, TimeOnly EndTime, int MaxCustomers, bool IsActive, DateTimeOffset? UpdatedAt, bool HasAppointments);

/// <summary>Thông tin khung giờ được quản lý tạo hoặc cập nhật.</summary>
public sealed record SaveManagedTimeSlotRequest(
    [property: JsonRequired] TimeOnly StartTime,
    [property: JsonRequired] TimeOnly EndTime,
    [property: JsonRequired] int MaxCustomers,
    [property: JsonRequired] bool IsActive,
    DateTimeOffset? ExpectedUpdatedAt);
