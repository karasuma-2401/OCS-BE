namespace OCS.Api.Consts;

/// <summary>
/// Mã lỗi nghiệp vụ cho các API hồ sơ người ký gửi.
/// </summary>
public static class CustomerErrorCodes
{
    public const string ProfileNotFound = "CUSTOMER.PROFILE_NOT_FOUND";
    public const string PhoneNumberExists = "CUSTOMER.PHONE_NUMBER_EXISTS";
    public const string BankAccountLockedForPayout = "CUSTOMER.BANK_ACCOUNT_LOCKED_FOR_PAYOUT";
}
