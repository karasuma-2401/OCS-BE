namespace OCS.Domain.Enums;

public enum AppointmentStatus
{
    Scheduled = 0,
    Cancelled = 1,
    Completed = 2
}

public enum SubmissionMethod
{
    InPerson = 0,
    Shipping = 1
}

public enum ExpiryHandling
{
    Return = 0,
    Donate = 1
}

public enum ConsignmentContractStatus
{
    Active = 0,
    Completed = 1
}

public enum BuyoutStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public enum ProductSourceType
{
    Consignment = 0,
    Buyout = 1
}

public enum ProductStatus
{
    PendingProcessing = 0,
    OnSale = 1,
    Sold = 2,
    Expired = 3,
    Donated = 4,
    Returned = 5,
    EarlyWithdrawn = 6,
    Compensated = 7,
    Cancelled = 8
}