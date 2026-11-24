namespace OCS.Api.Consts;

public enum InvoicePaymentStatus
{
    Unpaid = 0,
    Partial = 1,
    Paid = 2
}

public enum PayoutStatus
{
    Pending = 0,
    Approved = 1,
    Transferred = 2,
    Completed = 3,
    Rejected = 4
}

public enum PayoutDisputeStatus
{
    Open = 0,
    Resolved = 1,
    Rejected = 2
}

public enum PayoutLineType
{
    Commission = 0,
    Compensation = 1,
    ServiceFee = 2,
    SaleProceeds = 3
}

public enum FinancialTransactionType
{
    Income = 0,
    Expense = 1,
    CashFloat = 2,
    BankDeposit = 3
}

public enum PaymentMethod
{
    Cash = 0,
    VietQr = 1,
    BankTransfer = 2
}

public enum FinancialTransactionStatus
{
    Pending = 0,
    Success = 1,
    Failed = 2,
    Refunded = 3
}
