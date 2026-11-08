using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using OCS.Api.Common;
using OCS.Api.Configuration;
using OCS.Api.Consts;
using OCS.Api.Interfaces.Services;

namespace OCS.Api.Services;

public sealed partial class VietQrQuickLinkAdapter(IOptions<VietQrOptions> options) : IPaymentGatewayAdapter
{
    public const string AdapterProviderName = "VietQrQuickLink";

    public string ProviderName => AdapterProviderName;

    public Task<Result<QrPaymentAdapterResult>> CreateQrPaymentAsync(
        QrPaymentAdapterRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Amount <= 0 || decimal.Round(request.Amount, 2) != request.Amount)
        {
            return Task.FromResult(Result.Failure<QrPaymentAdapterResult>(
                ApplicationError.Validation("PAYMENT.INVALID_AMOUNT", "Số tiền tạo mã VietQR phải lớn hơn 0.")));
        }

        var normalizedRef = NormalizeTransferReference(request.PaymentReference);
        if (string.IsNullOrWhiteSpace(normalizedRef) || normalizedRef.Length > 25)
        {
            return Task.FromResult(Result.Failure<QrPaymentAdapterResult>(
                ApplicationError.Validation("PAYMENT.INVALID_REFERENCE", "Mã tham chiếu thanh toán không hợp lệ.")));
        }

        var cfg = options.Value;
        var bankCode = NormalizeAlphaNumeric(cfg.BankCode);
        var accountNumber = NormalizeAlphaNumeric(cfg.AccountNumber);
        var accountHolderName = NormalizeAccountHolderName(cfg.AccountHolderName);
        var template = string.IsNullOrWhiteSpace(cfg.Template) ? "compact2" : cfg.Template.Trim();
        var baseUrl = string.IsNullOrWhiteSpace(cfg.QuickLinkBaseUrl)
            ? "https://img.vietqr.io/image"
            : cfg.QuickLinkBaseUrl.Trim().TrimEnd('/');

        if (string.IsNullOrWhiteSpace(bankCode) || string.IsNullOrWhiteSpace(accountNumber) || string.IsNullOrWhiteSpace(accountHolderName))
        {
            return Task.FromResult(Result.Failure<QrPaymentAdapterResult>(
                ApplicationError.Validation("PAYMENT.MISCONFIGURED_BANK", "Chưa cấu hình đầy đủ thông tin tài khoản ngân hàng nhận VietQR.")));
        }

        var amountIntegerOrDecimal = request.Amount == decimal.Truncate(request.Amount)
            ? decimal.Truncate(request.Amount).ToString("0", CultureInfo.InvariantCulture)
            : request.Amount.ToString("0.00", CultureInfo.InvariantCulture);

        var qrCodeUrl =
            $"{baseUrl}/{Uri.EscapeDataString(bankCode)}-{Uri.EscapeDataString(accountNumber)}-{Uri.EscapeDataString(template)}.png" +
            $"?amount={Uri.EscapeDataString(amountIntegerOrDecimal)}" +
            $"&addInfo={Uri.EscapeDataString(normalizedRef)}" +
            $"&accountName={Uri.EscapeDataString(accountHolderName)}";

        var qrPayload =
            $"vietqr://{bankCode}/{accountNumber}?amount={request.Amount.ToString("0.00", CultureInfo.InvariantCulture)}&addInfo={normalizedRef}&accountName={Uri.EscapeDataString(accountHolderName)}";

        var result = new QrPaymentAdapterResult(
            ProviderName: ProviderName,
            PaymentReference: normalizedRef,
            TransferContent: normalizedRef,
            BankCode: bankCode,
            BankAccountNumber: accountNumber,
            BankAccountHolderName: accountHolderName,
            QrCodeUrl: qrCodeUrl,
            QrPayload: qrPayload);

        return Task.FromResult(Result.Success(result));
    }

    public Task<Result<QrPaymentProviderStatusResult>> QueryPaymentStatusAsync(
        string paymentReference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedRef = NormalizeTransferReference(paymentReference);
        if (string.IsNullOrWhiteSpace(normalizedRef))
        {
            return Task.FromResult(Result.Failure<QrPaymentProviderStatusResult>(
                ApplicationError.Validation("PAYMENT.INVALID_REFERENCE", "Mã tham chiếu thanh toán không hợp lệ.")));
        }

        // VietQrQuickLink relies on webhook/provider reconciliation to transition from Pending to Success.
        // Creating or querying the QR link alone never marks the payment as received.
        return Task.FromResult(Result.Success(new QrPaymentProviderStatusResult(
            ProviderName: ProviderName,
            PaymentReference: normalizedRef,
            Status: FinancialTransactionStatus.Pending)));
    }

    public static string NormalizeTransferReference(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var upper = RemoveDiacritics(input).Trim().ToUpperInvariant();
        return NonAlphaNumericRegex().Replace(upper, string.Empty);
    }

    private static string NormalizeAlphaNumeric(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        return NonAlphaNumericRegex().Replace(RemoveDiacritics(input).Trim().ToUpperInvariant(), string.Empty);
    }

    private static string NormalizeAccountHolderName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var ascii = RemoveDiacritics(input).Trim().ToUpperInvariant();
        return MultipleSpacesRegex().Replace(NonHolderCharsRegex().Replace(ascii, " "), " ").Trim();
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c switch
                {
                    'đ' => 'd',
                    'Đ' => 'D',
                    _ => c
                });
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex("[^A-Z0-9]", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphaNumericRegex();

    [GeneratedRegex("[^A-Z0-9 ]", RegexOptions.CultureInvariant)]
    private static partial Regex NonHolderCharsRegex();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex MultipleSpacesRegex();
}
