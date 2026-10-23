namespace OCS.Domain.Enums;

public enum ExpiryProcessingType
{
    Donate = 0,
    Return = 1
}

public enum ExpiryProcessingStatus
{
    Pending = 0,
    Completed = 1,
    OverdueDonated = 2
}

public enum IncidentType
{
    Lost = 0,
    Damaged = 1,
    TagSwapped = 2
}

public enum IncidentStatus
{
    Pending = 0,
    Approved = 1,
    Compensated = 2
}

public enum NotificationType
{
    System = 0,
    Manual = 1,
    Promotion = 2
}
