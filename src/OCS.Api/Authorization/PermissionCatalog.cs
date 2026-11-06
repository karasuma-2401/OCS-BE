namespace OCS.Api.Authorization;

public static class PermissionCatalog
{
    public const string ManagePermissions = "permissions.manage";
    public const string AccountsRead = "accounts.read";
    public const string AccountsCreate = "accounts.create";
    public const string AccountsUpdate = "accounts.update";
    public const string AccountsStatus = "accounts.status";
    public const string AccountsRoles = "accounts.roles";
    public const string AccountsHistory = "accounts.history";
    public const string AccountsResetPassword = "accounts.reset-password";
    public const string CustomersRead = "customers.read";
    public const string AppointmentsCreate = "appointments.create";
    public const string AppointmentsOwnRead = "appointments.own.read";
    public const string AppointmentsOwnCancel = "appointments.own.cancel";
    public const string AppointmentsStaffRead = "appointments.staff.read";
    public const string AppointmentsStaffUpdate = "appointments.staff.update";
    public const string AppointmentsStaffReception = "appointments.staff.reception";
    public const string ProductsCreate = "products.create";
    public const string ScreenPos = "screens.pos";
    public const string ScreenInventory = "screens.inventory";
    public const string ScreenConsignment = "screens.consignment";
    public const string ScreenCustomers = "screens.customers";
    public const string ScreenSettlement = "screens.settlement";
    public const string ScreenReports = "screens.reports";
    public const string ScreenSettings = "screens.settings";
    public const string ScreenAccounts = "screens.accounts";

    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(CustomersRead, "Xem hồ sơ khách", "Tra cứu hồ sơ khách hàng.", 1),
        new(AccountsCreate, "Tạo tài khoản nội bộ", "Cấp tài khoản nhân viên.", 2),
        new(AppointmentsCreate, "Đặt lịch ký gửi", "Khách hàng tạo lịch hẹn của mình.", 3),
        new(ManagePermissions, "Quản lý phân quyền", "Xem và chỉnh quyền của các vai trò.", 4),
        new(AccountsRead, "Xem tài khoản", "Xem danh sách và chi tiết tài khoản.", 5),
        new(AccountsUpdate, "Sửa thông tin tài khoản", "Cập nhật thông tin và hồ sơ nhân viên.", 6),
        new(AccountsStatus, "Đổi trạng thái tài khoản", "Khóa, mở khóa và vô hiệu hóa tài khoản.", 7),
        new(AccountsRoles, "Gán vai trò tài khoản", "Thay đổi vai trò của tài khoản nội bộ.", 8),
        new(AccountsHistory, "Xem lịch sử tài khoản", "Xem nhật ký thay đổi tài khoản.", 9),
        new(AccountsResetPassword, "Đặt lại mật khẩu", "Đặt lại mật khẩu cho tài khoản.", 10),
        new(AppointmentsStaffRead, "Xem lịch hẹn tại cửa hàng", "Xem lịch hẹn của khách tại cửa hàng.", 11),
        new(AppointmentsStaffUpdate, "Cập nhật lịch hẹn", "Cập nhật trạng thái lịch hẹn.", 12),
        new(AppointmentsStaffReception, "Tiếp nhận lịch hẹn", "Bắt đầu tiếp nhận từ lịch hẹn.", 13),
        new(ProductsCreate, "Tạo sản phẩm", "Thêm sản phẩm vào kho.", 14),
        new(ScreenPos, "Màn hình POS", "Hiển thị tab bán hàng POS.", 15),
        new(ScreenInventory, "Màn hình Kho hàng", "Hiển thị tab kho hàng.", 16),
        new(ScreenConsignment, "Màn hình Ký gửi", "Hiển thị tab tiếp nhận ký gửi.", 17),
        new(ScreenCustomers, "Màn hình Khách hàng", "Hiển thị tab khách hàng.", 18),
        new(ScreenSettlement, "Màn hình Quyết toán", "Hiển thị tab quyết toán.", 19),
        new(ScreenReports, "Màn hình Báo cáo", "Hiển thị tab báo cáo.", 20),
        new(ScreenSettings, "Màn hình Cấu hình", "Hiển thị tab cấu hình.", 21),
        new(ScreenAccounts, "Màn hình Tài khoản", "Hiển thị tab quản lý tài khoản.", 22),
        new(AppointmentsOwnRead, "Xem lịch hẹn của mình", "Khách hàng xem lịch hẹn của chính mình.", 23),
        new(AppointmentsOwnCancel, "Hủy lịch hẹn của mình", "Khách hàng hủy lịch hẹn của chính mình.", 24)
    ];

    public static readonly IReadOnlySet<string> AdminOnly = new HashSet<string>(StringComparer.Ordinal)
    {
        ManagePermissions, AccountsRoles, AccountsHistory, AccountsResetPassword, ScreenSettings
    };

    public static readonly IReadOnlySet<string> RequiredAdminCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        ManagePermissions, ScreenSettings, AccountsRead, AccountsRoles, ScreenAccounts
    };

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultRolePermissions =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["Admin"] = RequiredAdminCodes.ToArray(),
            ["Manager"] =
            [
                CustomersRead, AccountsCreate, AccountsRead, AccountsUpdate, AccountsStatus,
                AppointmentsStaffRead, AppointmentsStaffUpdate, AppointmentsStaffReception,
                ProductsCreate, ScreenPos, ScreenInventory, ScreenConsignment, ScreenCustomers,
                ScreenSettlement, ScreenReports, ScreenAccounts
            ],
            ["Staff"] =
            [
                CustomersRead, AppointmentsStaffRead, AppointmentsStaffUpdate,
                AppointmentsStaffReception, ProductsCreate, ScreenPos, ScreenInventory,
                ScreenConsignment, ScreenCustomers, ScreenSettlement, ScreenReports
            ],
            ["Customer"] = [AppointmentsCreate, AppointmentsOwnRead, AppointmentsOwnCancel]
        };

    public static IReadOnlyList<string> DefaultsFor(string roleCode) =>
        DefaultRolePermissions.TryGetValue(roleCode, out var permissions) ? permissions : [];

    public static readonly IReadOnlySet<string> KnownCodes = All.Select(item => item.Code)
        .ToHashSet(StringComparer.Ordinal);

    public static bool CanAssign(string roleCode, string permissionCode) => KnownCodes.Contains(permissionCode) && (roleCode switch
    {
        "Admin" => permissionCode != AppointmentsCreate
            && !permissionCode.StartsWith("appointments.own.", StringComparison.Ordinal),
        "Customer" => permissionCode is AppointmentsCreate or AppointmentsOwnRead or AppointmentsOwnCancel,
        "Manager" => !permissionCode.StartsWith("appointments.own.", StringComparison.Ordinal)
            && permissionCode != AppointmentsCreate && !AdminOnly.Contains(permissionCode),
        "Staff" => !permissionCode.StartsWith("appointments.own.", StringComparison.Ordinal)
            && permissionCode != AppointmentsCreate && !AdminOnly.Contains(permissionCode)
            && !permissionCode.StartsWith("accounts.", StringComparison.Ordinal)
            && permissionCode != ScreenAccounts,
        _ => false
    });

    public static Guid IdFor(int number) => Guid.Parse($"30000000-0000-0000-0000-{number:000000000000}");
}

public sealed record PermissionDefinition(string Code, string Name, string Description, int Number);
