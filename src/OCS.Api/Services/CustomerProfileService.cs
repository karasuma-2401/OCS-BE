using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Customers;

namespace OCS.Api.Services;

public sealed partial class CustomerProfileService(IUnitOfWork unitOfWork) : ICustomerProfileService
{
    [GeneratedRegex(@"^0[0-9]{9}$")]
    private static partial Regex PhoneNumberRegex();

    public async Task<Result<CustomerSelfProfileResponse>> GetSelfProfileAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var account = await unitOfWork.CustomerProfileRepository.GetSelfProfileAsync(customerId, cancellationToken);
        if (account is null || account.CustomerProfile is null)
        {
            return Result.Failure<CustomerSelfProfileResponse>(
                ApplicationError.NotFound(CustomerErrorCodes.ProfileNotFound, "Không tìm thấy hồ sơ người ký gửi."));
        }

        return Result.Success(MapToResponse(account));
    }

    public async Task<Result<CustomerSelfProfileResponse>> UpdateSelfProfileAsync(
        Guid customerId,
        UpdateCustomerSelfProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trimmedFullName = request.FullName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmedFullName) || trimmedFullName.Length > 150)
        {
            return Result.Failure<CustomerSelfProfileResponse>(
                ApplicationError.Validation(ApiErrorCodes.ValidationFailed, "Họ và tên không được để trống và tối đa 150 ký tự."));
        }

        var trimmedPhoneNumber = request.PhoneNumber?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmedPhoneNumber) || !PhoneNumberRegex().IsMatch(trimmedPhoneNumber))
        {
            return Result.Failure<CustomerSelfProfileResponse>(
                ApplicationError.Validation(ApiErrorCodes.ValidationFailed, "Số điện thoại phải gồm đúng 10 chữ số bắt đầu bằng số 0."));
        }

        string? newBankCode = null;
        string? newAccountNumber = null;
        string? newAccountHolderName = null;

        if (request.BankAccount is not null)
        {
            newBankCode = request.BankAccount.BankCode?.Trim().ToUpperInvariant();
            newAccountNumber = request.BankAccount.AccountNumber?.Trim();
            newAccountHolderName = request.BankAccount.AccountHolderName?.Trim();

            if (string.IsNullOrWhiteSpace(newBankCode)
                || string.IsNullOrWhiteSpace(newAccountNumber)
                || string.IsNullOrWhiteSpace(newAccountHolderName))
            {
                return Result.Failure<CustomerSelfProfileResponse>(
                    ApplicationError.Validation(ApiErrorCodes.ValidationFailed, "Thông tin ngân hàng phải có đầy đủ mã ngân hàng, số tài khoản và tên chủ tài khoản."));
            }

            if (newBankCode.Length > 50 || newAccountNumber.Length > 50 || newAccountHolderName.Length > 200)
            {
                return Result.Failure<CustomerSelfProfileResponse>(
                    ApplicationError.Validation(ApiErrorCodes.ValidationFailed, "Thông tin ngân hàng vượt quá độ dài quy định."));
            }
        }

        var account = await unitOfWork.CustomerProfileRepository.GetSelfProfileForUpdateAsync(customerId, cancellationToken);
        if (account is null || account.CustomerProfile is null)
        {
            return Result.Failure<CustomerSelfProfileResponse>(
                ApplicationError.NotFound(CustomerErrorCodes.ProfileNotFound, "Không tìm thấy hồ sơ người ký gửi."));
        }

        var currentBankCode = string.IsNullOrWhiteSpace(account.CustomerProfile.BankCode) ? null : account.CustomerProfile.BankCode.Trim().ToUpperInvariant();
        var currentAccountNumber = string.IsNullOrWhiteSpace(account.CustomerProfile.BankAccountNumber) ? null : account.CustomerProfile.BankAccountNumber.Trim();
        var currentAccountHolderName = string.IsNullOrWhiteSpace(account.CustomerProfile.BankAccountHolderName) ? null : account.CustomerProfile.BankAccountHolderName.Trim();

        var isBankChanged = !string.Equals(currentBankCode, newBankCode, StringComparison.Ordinal)
                         || !string.Equals(currentAccountNumber, newAccountNumber, StringComparison.Ordinal)
                         || !string.Equals(currentAccountHolderName, newAccountHolderName, StringComparison.Ordinal);

        if (isBankChanged)
        {
            var hasPendingPayout = await unitOfWork.CustomerProfileRepository.HasPendingPayoutAsync(customerId, cancellationToken);
            if (hasPendingPayout)
            {
                return Result.Failure<CustomerSelfProfileResponse>(
                    ApplicationError.Conflict(CustomerErrorCodes.BankAccountLockedForPayout, "Không thể thay đổi thông tin ngân hàng khi đang có yêu cầu giải ngân chờ xử lý."));
            }
        }

        if (!string.Equals(account.PhoneNumber, trimmedPhoneNumber, StringComparison.Ordinal))
        {
            var isTaken = await unitOfWork.CustomerProfileRepository.IsPhoneNumberTakenAsync(trimmedPhoneNumber, customerId, cancellationToken);
            if (isTaken)
            {
                return Result.Failure<CustomerSelfProfileResponse>(
                    ApplicationError.Conflict(CustomerErrorCodes.PhoneNumberExists, "Số điện thoại đã được sử dụng bởi một tài khoản khách hàng khác."));
            }
        }

        account.FullName = trimmedFullName;
        account.PhoneNumber = trimmedPhoneNumber;

        account.CustomerProfile.BankCode = newBankCode;
        account.CustomerProfile.BankAccountNumber = newAccountNumber;
        account.CustomerProfile.BankAccountHolderName = newAccountHolderName;

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            return Result.Failure<CustomerSelfProfileResponse>(
                ApplicationError.Conflict(CustomerErrorCodes.PhoneNumberExists, "Số điện thoại đã được sử dụng bởi một tài khoản khách hàng khác."));
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        return Result.Success(MapToResponse(account));
    }

    private static CustomerSelfProfileResponse MapToResponse(Account account)
    {
        BankAccountResponse? bankAccount = null;
        var profile = account.CustomerProfile;

        if (profile is not null
            && !string.IsNullOrWhiteSpace(profile.BankCode)
            && !string.IsNullOrWhiteSpace(profile.BankAccountNumber)
            && !string.IsNullOrWhiteSpace(profile.BankAccountHolderName))
        {
            bankAccount = new BankAccountResponse(
                profile.BankCode.Trim().ToUpperInvariant(),
                profile.BankAccountNumber.Trim(),
                profile.BankAccountHolderName.Trim());
        }

        return new CustomerSelfProfileResponse(
            account.Id,
            account.FullName,
            account.PhoneNumber,
            bankAccount);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
        || ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true
        || ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase);
}
