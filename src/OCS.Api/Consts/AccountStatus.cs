namespace OCS.Api.Consts;

public enum AccountStatus
{
    PendingActivation = 0,  // chua kich hoat
    Active = 1,             // dang su dung,
    Locked = 2,
    Deactivated = 3
}

public enum CustomerStatus
{
    Active = 0,
    Suspended = 1
}

public enum EmployeeStatus
{
    Working = 0,
    Suspended = 1,
    Left = 2
}