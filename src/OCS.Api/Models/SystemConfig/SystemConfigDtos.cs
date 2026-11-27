namespace OCS.Api.Models.SystemConfig;

public sealed record CommissionTierRatesDto(
    decimal NoBrand,
    decimal LocalBrand,
    decimal Commercial,
    decimal Luxury);

public sealed record SystemConfigDto(
    int ConsignmentDurationDays,
    CommissionTierRatesDto CommissionTierRates,
    decimal EarlyWithdrawalFeeRate,
    int PayoutAutoConfirmHours,
    string Version);

public sealed record ReceptionDefaultsDto(
    int ConsignmentDurationDays,
    CommissionTierRatesDto CommissionTierRates);

public sealed record UpdateSystemConfigRequest(
    int ConsignmentDurationDays,
    CommissionTierRatesDto CommissionTierRates,
    decimal EarlyWithdrawalFeeRate,
    int PayoutAutoConfirmHours,
    string Version);
