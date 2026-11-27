namespace OCS.Api.Consts;

public static class SystemParameterKeys
{
    public const string PayoutAutoConfirmHours = "payout.auto_confirm_hours";
    public const int DefaultAutoConfirmHours = 48;
    public const int MinAutoConfirmHours = 1;
    public const int MaxAutoConfirmHours = 720;

    public const string ConsignmentDurationDays = "consignment.default_duration_days";
    public const int DefaultConsignmentDurationDays = 30;
    public const int MinConsignmentDurationDays = 1;
    public const int MaxConsignmentDurationDays = 365;

    public const string CommissionTierRates = "commission.tier_rates";

    public const string EarlyWithdrawalFeeRate = "early_withdrawal.default_fee_rate";
    public const decimal DefaultEarlyWithdrawalFeeRate = 10m;
}
