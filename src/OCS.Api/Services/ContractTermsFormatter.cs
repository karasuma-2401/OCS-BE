using System.Globalization;
using System.Text.RegularExpressions;
using OCS.Api.Common;

namespace OCS.Api.Services;

public static partial class ContractTermsFormatter
{
    public const int MaxPersistedTermsLength = 6000;
    public const int MaxTemplateDefaultTermsLength = 5800;
    public const string FeeRatePlaceholder = "{{EARLY_WITHDRAWAL_FEE_RATE}}";

    [GeneratedRegex(@"(?<!\d)\.(?!\d)|\r?\n", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceBoundaryPattern();

    [GeneratedRegex(@"rút(?:\s+hàng)?\s+sớm", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EarlyWithdrawalKeywordPattern();

    [GeneratedRegex(@"(?:(?:tỷ lệ\s+)?phí\s+rút(?:\s+hàng)?\s+sớm|(?:chịu|mức|áp dụng)\s+(?:tỷ lệ\s+)?phí)\s*(?:là|bằng|:)?\s*(\d+(?:[.,]\d+)?)\s*%", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FeePercentagePattern();

    [GeneratedRegex(@"hoa hồng", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CommissionKeywordPattern();

    [GeneratedRegex(@"(?:tỷ lệ\s+)?hoa hồng(?:\s+đã\s+thỏa\s+thuận)?(?:\s+của\s+(?:từng\s+)?sản\s+phẩm)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegacyCommissionPhrasePattern();

    [GeneratedRegex(@"(?:tỷ lệ\s+)?phí(?!\s+(?:vận chuyển|giao hàng|bảo hiểm|đóng gói|lưu kho))|tỷ lệ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FeeOrRateKeywordPattern();

    public static string FormatWithdrawalFeeClause(decimal earlyWithdrawalFeeRate)
    {
        var rateText = earlyWithdrawalFeeRate.ToString("0.##", CultureInfo.InvariantCulture);
        return $"Phí rút hàng sớm của mỗi sản phẩm bằng giá niêm yết nhân với tỷ lệ phí rút sớm {rateText}% trên hợp đồng; làm tròn đến 2 chữ số thập phân.";
    }

    public static Result<string> BuildEffectiveTerms(string? baseTerms, decimal earlyWithdrawalFeeRate, bool isCustomApprovedTerms)
    {
        var trimmedBase = string.IsNullOrWhiteSpace(baseTerms) ? null : baseTerms.Trim();
        var expectedRateText = earlyWithdrawalFeeRate.ToString("0.##", CultureInfo.InvariantCulture);
        var expectedRateRounded = decimal.Round(earlyWithdrawalFeeRate, 2, MidpointRounding.AwayFromZero);

        if (trimmedBase is not null && trimmedBase.Contains(FeeRatePlaceholder, StringComparison.Ordinal))
        {
            trimmedBase = trimmedBase.Replace(FeeRatePlaceholder, expectedRateText, StringComparison.Ordinal);
        }

        if (isCustomApprovedTerms && trimmedBase is not null)
        {
            var hasMatchingFeeClause = false;
            foreach (var segment in SentenceBoundaryPattern().Split(trimmedBase))
            {
                var sentence = segment.Trim();
                if (string.IsNullOrWhiteSpace(sentence) || !EarlyWithdrawalKeywordPattern().IsMatch(sentence))
                    continue;

                if (CommissionKeywordPattern().IsMatch(sentence))
                {
                    return Result.Failure<string>(ApplicationError.Validation(
                        "CONTRACT.TERMS_FEE_RATE_MISMATCH",
                        $"Điều khoản đã duyệt đang tính phí rút hàng sớm theo tỷ lệ hoa hồng, không tương thích với tỷ lệ phí rút sớm {expectedRateText}% trên hợp đồng."));
                }

                var percentMatches = FeePercentagePattern().Matches(sentence);
                if (percentMatches.Count > 0)
                {
                    foreach (Match match in percentMatches)
                    {
                        var numGroup = match.Groups[1].Success ? match.Groups[1] : match.Groups[2];
                        var raw = numGroup.Value.Replace(',', '.');
                        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var mentionedRate) ||
                            decimal.Round(mentionedRate, 2, MidpointRounding.AwayFromZero) != expectedRateRounded)
                        {
                            return Result.Failure<string>(ApplicationError.Validation(
                                "CONTRACT.TERMS_FEE_RATE_MISMATCH",
                                $"Điều khoản đã duyệt ghi tỷ lệ phí rút sớm {numGroup.Value}% không khớp với tỷ lệ {expectedRateText}% đang áp dụng trên hợp đồng."));
                        }
                    }

                    hasMatchingFeeClause = true;
                }
                else if (FeeOrRateKeywordPattern().IsMatch(sentence))
                {
                    return Result.Failure<string>(ApplicationError.Validation(
                        "CONTRACT.TERMS_FEE_RATE_MISMATCH",
                        $"Điều khoản đã duyệt đề cập phí rút hàng sớm nhưng chưa ghi rõ tỷ lệ {expectedRateText}% đang áp dụng trên hợp đồng."));
                }
            }

            if (hasMatchingFeeClause)
                return ValidateMaxLength(trimmedBase, appended: false);
        }

        if (!isCustomApprovedTerms && trimmedBase is not null && EarlyWithdrawalKeywordPattern().IsMatch(trimmedBase))
        {
            var hasResolvedFeeClause = false;
            var updatedLines = new List<string>();
            foreach (var line in trimmedBase.Split(["\r\n", "\n"], StringSplitOptions.None))
            {
                var segments = SentenceBoundaryPattern().Split(line);
                var updatedSegments = new List<string>();
                foreach (var seg in segments)
                {
                    var s = seg.Trim();
                    if (string.IsNullOrWhiteSpace(s))
                        continue;

                    if (EarlyWithdrawalKeywordPattern().IsMatch(s))
                    {
                        if (CommissionKeywordPattern().IsMatch(s))
                        {
                            s = LegacyCommissionPhrasePattern().Replace(s, $"tỷ lệ phí rút sớm {expectedRateText}% trên hợp đồng");
                            hasResolvedFeeClause = true;
                        }
                        else if (FeePercentagePattern().IsMatch(s))
                        {
                            s = FeePercentagePattern().Replace(s, match =>
                            {
                                var numGroup = match.Groups[1].Success ? match.Groups[1] : match.Groups[2];
                                var relStart = numGroup.Index - match.Index;
                                return string.Concat(
                                    match.Value.AsSpan(0, relStart),
                                    expectedRateText,
                                    match.Value.AsSpan(relStart + numGroup.Length));
                            });
                            hasResolvedFeeClause = true;
                        }
                        else if (FeeOrRateKeywordPattern().IsMatch(s))
                        {
                            return Result.Failure<string>(ApplicationError.Validation(
                                "CONTRACT.TERMS_FEE_RATE_MISMATCH",
                                $"Điều khoản mẫu đề cập phí rút hàng sớm nhưng chưa ghi rõ tỷ lệ {expectedRateText}% đang áp dụng trên hợp đồng."));
                        }
                    }

                    updatedSegments.Add(s);
                }

                if (updatedSegments.Count > 0)
                    updatedLines.Add(string.Join(". ", updatedSegments) + (line.TrimEnd().EndsWith('.') ? "." : string.Empty));
            }

            trimmedBase = updatedLines.Count == 0 ? null : string.Join("\n", updatedLines);
            if (hasResolvedFeeClause && trimmedBase is not null)
                return ValidateMaxLength(trimmedBase, appended: false);
        }

        var clause = FormatWithdrawalFeeClause(earlyWithdrawalFeeRate);
        var effective = trimmedBase is null ? clause : $"{trimmedBase}\n{clause}";
        return ValidateMaxLength(effective, appended: true);
    }

    private static Result<string> ValidateMaxLength(string terms, bool appended)
    {
        if (terms.Length <= MaxPersistedTermsLength)
            return Result.Success(terms);

        var detail = appended
            ? $"Tổng độ dài điều khoản hợp đồng sau khi bổ sung điều khoản phí rút sớm ({terms.Length} ký tự) vượt quá giới hạn {MaxPersistedTermsLength} ký tự."
            : $"Tổng độ dài điều khoản hợp đồng ({terms.Length} ký tự) vượt quá giới hạn {MaxPersistedTermsLength} ký tự.";
        return Result.Failure<string>(ApplicationError.Validation("CONTRACT.TERMS_TOO_LONG", detail));
    }
}
