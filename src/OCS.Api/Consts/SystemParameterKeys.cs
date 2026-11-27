namespace OCS.Api.Consts;

public static class SystemParameterKeys
{
    public const string PayoutAutoConfirmHours = "payout.auto_confirm_hours";
    public const int DefaultAutoConfirmHours = 48;
    public const int MinAutoConfirmHours = 1;
    public const int MaxAutoConfirmHours = 720;
}
