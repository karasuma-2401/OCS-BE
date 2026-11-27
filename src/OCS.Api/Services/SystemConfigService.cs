using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Models.SystemConfig;

namespace OCS.Api.Services;

public sealed class SystemConfigService(AppDbContext db)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly CommissionTierRatesDto DefaultCommissionTiers = new(
        NoBrand: 10m,
        LocalBrand: 12m,
        Commercial: 15m,
        Luxury: 20m);

    private static readonly string[] ConfigKeys =
    [
        SystemParameterKeys.ConsignmentDurationDays,
        SystemParameterKeys.CommissionTierRates,
        SystemParameterKeys.EarlyWithdrawalFeeRate,
        SystemParameterKeys.PayoutAutoConfirmHours
    ];

    public async Task<Result<SystemConfigDto>> GetAsync(CancellationToken ct) =>
        Result.Success(await ReadEffectiveAsync(db, ct));

    public async Task<Result<ReceptionDefaultsDto>> GetReceptionDefaultsAsync(CancellationToken ct)
    {
        var cfg = await ReadEffectiveAsync(db, ct);
        return Result.Success(new ReceptionDefaultsDto(cfg.ConsignmentDurationDays, cfg.CommissionTierRates));
    }

    public static async Task<SystemConfigDto> ReadEffectiveAsync(AppDbContext db, CancellationToken ct)
    {
        var parameters = await db.SystemParameters.AsNoTracking()
            .Where(x => ConfigKeys.Contains(x.Key))
            .ToDictionaryAsync(x => x.Key, x => x.Value, StringComparer.Ordinal, ct);

        return BuildDto(parameters);
    }

    public static decimal GetTierCommissionRate(CommissionTierRatesDto tiers, BrandTier tier) => tier switch
    {
        BrandTier.NoBrand => tiers.NoBrand,
        BrandTier.LocalBrand => tiers.LocalBrand,
        BrandTier.Commercial => tiers.Commercial,
        BrandTier.Luxury => tiers.Luxury,
        _ => tiers.Commercial
    };

    public async Task<Result<SystemConfigDto>> UpdateAsync(UpdateSystemConfigRequest request, CancellationToken ct)
    {
        if (Validate(request) is { } validationError)
            return Result.Failure<SystemConfigDto>(validationError);

        if (string.IsNullOrWhiteSpace(request.Version))
            return Result.Failure<SystemConfigDto>(ApplicationError.Validation("SYSTEM_CONFIG.VERSION", "Thiếu phiên bản cấu hình hiện tại."));

        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;

        var existingRows = await db.SystemParameters
            .Where(x => ConfigKeys.Contains(x.Key))
            .ToDictionaryAsync(x => x.Key, StringComparer.Ordinal, ct);

        var currentDto = BuildDto(existingRows.ToDictionary(kv => kv.Key, kv => kv.Value.Value, StringComparer.Ordinal));
        if (!string.Equals(currentDto.Version, request.Version.Trim(), StringComparison.Ordinal))
        {
            return Result.Failure<SystemConfigDto>(
                ApplicationError.Conflict("SYSTEM_CONFIG.CONFLICT", "Cấu hình hệ thống đã được cập nhật bởi phiên khác. Vui lòng tải lại trước khi lưu."));
        }

        var nextValues = new Dictionary<string, (string Value, string Description)>(StringComparer.Ordinal)
        {
            [SystemParameterKeys.ConsignmentDurationDays] = (
                request.ConsignmentDurationDays.ToString(CultureInfo.InvariantCulture),
                "Thời hạn ký gửi mặc định (ngày)"
            ),
            [SystemParameterKeys.CommissionTierRates] = (
                JsonSerializer.Serialize(NormalizeTiers(request.CommissionTierRates), JsonOptions),
                "Tỷ lệ hoa hồng mặc định theo hạng thương hiệu (%)"
            ),
            [SystemParameterKeys.EarlyWithdrawalFeeRate] = (
                decimal.Round(request.EarlyWithdrawalFeeRate, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture),
                "Tỷ lệ phí rút hàng sớm mặc định (%)"
            ),
            [SystemParameterKeys.PayoutAutoConfirmHours] = (
                request.PayoutAutoConfirmHours.ToString(CultureInfo.InvariantCulture),
                "Thời gian tự động xác nhận tất toán sau chuyển khoản (giờ)"
            )
        };

        foreach (var (key, (value, description)) in nextValues)
        {
            if (existingRows.TryGetValue(key, out var row))
            {
                row.Value = value;
                row.Description = description;
            }
            else
            {
                var created = new SystemParameter { Key = key, Value = value, Description = description };
                db.SystemParameters.Add(created);
                existingRows[key] = created;
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<SystemConfigDto>(
                ApplicationError.Conflict("SYSTEM_CONFIG.CONFLICT", "Cấu hình hệ thống vừa thay đổi. Vui lòng tải lại trang và thử lại."));
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
            (pg.SqlState == PostgresErrorCodes.UniqueViolation || pg.SqlState == PostgresErrorCodes.SerializationFailure))
        {
            return Result.Failure<SystemConfigDto>(
                ApplicationError.Conflict("SYSTEM_CONFIG.CONFLICT", "Cấu hình hệ thống vừa được khởi tạo hoặc thay đổi ở phiên khác. Vui lòng tải lại và thử lại."));
        }
        catch (PostgresException pg) when (
            pg.SqlState == PostgresErrorCodes.UniqueViolation || pg.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            return Result.Failure<SystemConfigDto>(
                ApplicationError.Conflict("SYSTEM_CONFIG.CONFLICT", "Cấu hình hệ thống vừa được khởi tạo hoặc thay đổi ở phiên khác. Vui lòng tải lại và thử lại."));
        }

        var updated = BuildDto(existingRows.ToDictionary(kv => kv.Key, kv => kv.Value.Value, StringComparer.Ordinal));
        return Result.Success(updated);
    }

    private static ApplicationError? Validate(UpdateSystemConfigRequest request)
    {
        if (request.ConsignmentDurationDays is < SystemParameterKeys.MinConsignmentDurationDays or > SystemParameterKeys.MaxConsignmentDurationDays)
        {
            return ApplicationError.Validation(
                "SYSTEM_CONFIG.DURATION",
                $"Thời hạn ký gửi phải nằm trong khoảng {SystemParameterKeys.MinConsignmentDurationDays}–{SystemParameterKeys.MaxConsignmentDurationDays} ngày.");
        }

        if (request.PayoutAutoConfirmHours is < SystemParameterKeys.MinAutoConfirmHours or > SystemParameterKeys.MaxAutoConfirmHours)
        {
            return ApplicationError.Validation(
                "SYSTEM_CONFIG.AUTO_CONFIRM",
                $"Thời gian Auto-confirm phải nằm trong khoảng {SystemParameterKeys.MinAutoConfirmHours}–{SystemParameterKeys.MaxAutoConfirmHours} giờ.");
        }

        if (request.EarlyWithdrawalFeeRate is < 0m or > 100m)
        {
            return ApplicationError.Validation("SYSTEM_CONFIG.EARLY_WITHDRAWAL", "Phí rút sớm phải nằm trong khoảng 0–100%.");
        }

        var tiers = request.CommissionTierRates;
        if (tiers is null ||
            tiers.NoBrand is < 0m or > 100m ||
            tiers.LocalBrand is < 0m or > 100m ||
            tiers.Commercial is < 0m or > 100m ||
            tiers.Luxury is < 0m or > 100m)
        {
            return ApplicationError.Validation("SYSTEM_CONFIG.COMMISSION_TIERS", "Hoa hồng theo hạng phải nằm trong khoảng 0–100%.");
        }

        return null;
    }

    private static SystemConfigDto BuildDto(IReadOnlyDictionary<string, string> parameters)
    {
        var duration = ParseInt(
            parameters.GetValueOrDefault(SystemParameterKeys.ConsignmentDurationDays),
            SystemParameterKeys.DefaultConsignmentDurationDays,
            SystemParameterKeys.MinConsignmentDurationDays,
            SystemParameterKeys.MaxConsignmentDurationDays);

        var autoConfirm = ParseInt(
            parameters.GetValueOrDefault(SystemParameterKeys.PayoutAutoConfirmHours),
            SystemParameterKeys.DefaultAutoConfirmHours,
            SystemParameterKeys.MinAutoConfirmHours,
            SystemParameterKeys.MaxAutoConfirmHours);

        var withdrawalFee = ParsePercent(
            parameters.GetValueOrDefault(SystemParameterKeys.EarlyWithdrawalFeeRate),
            SystemParameterKeys.DefaultEarlyWithdrawalFeeRate);

        var tiers = ParseTiers(parameters.GetValueOrDefault(SystemParameterKeys.CommissionTierRates));

        var canonical = $"{duration}|{autoConfirm}|{withdrawalFee:0.##}|{tiers.NoBrand:0.##}|{tiers.LocalBrand:0.##}|{tiers.Commercial:0.##}|{tiers.Luxury:0.##}";
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        return new SystemConfigDto(duration, tiers, withdrawalFee, autoConfirm, version);
    }

    private static int ParseInt(string? raw, int fallback, int min, int max) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= min && parsed <= max
            ? parsed
            : fallback;

    private static decimal ParsePercent(string? raw, decimal fallback) =>
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed is >= 0m and <= 100m
            ? decimal.Round(parsed, 2, MidpointRounding.AwayFromZero)
            : fallback;

    private static CommissionTierRatesDto ParseTiers(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return DefaultCommissionTiers;
        try
        {
            var parsed = JsonSerializer.Deserialize<CommissionTierRatesDto>(raw, JsonOptions);
            return parsed is null ? DefaultCommissionTiers : NormalizeTiers(parsed);
        }
        catch (JsonException)
        {
            return DefaultCommissionTiers;
        }
    }

    private static CommissionTierRatesDto NormalizeTiers(CommissionTierRatesDto tiers) => new(
        NoBrand: ClampPercent(tiers.NoBrand, DefaultCommissionTiers.NoBrand),
        LocalBrand: ClampPercent(tiers.LocalBrand, DefaultCommissionTiers.LocalBrand),
        Commercial: ClampPercent(tiers.Commercial, DefaultCommissionTiers.Commercial),
        Luxury: ClampPercent(tiers.Luxury, DefaultCommissionTiers.Luxury));

    private static decimal ClampPercent(decimal value, decimal fallback) =>
        value is >= 0m and <= 100m ? decimal.Round(value, 2, MidpointRounding.AwayFromZero) : fallback;
}
