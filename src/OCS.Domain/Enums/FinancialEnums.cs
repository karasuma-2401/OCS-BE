namespace OCS.Domain.Enums;

public enum InvoicePaymentStatus
{
    Unpaid = 0,
    Partial = 1,
    Paid = 2
}

public enum PayoutStatus
{
    Pending = 0,
    Transferred = 1,
    Completed = 2
}

public enum PayoutLineType
{
    Commission = 0,
    Compensation = 1,
    ServiceFee = 2
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
