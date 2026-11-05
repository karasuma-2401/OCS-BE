using Microsoft.EntityFrameworkCore;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;

namespace OCS.Api.Repositories;

public sealed class CustomerProfileRepository(AppDbContext dbContext) : ICustomerProfileRepository
{
    public Task<Account?> GetSelfProfileAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        dbContext.Accounts
            .AsNoTracking()
            .Include(a => a.CustomerProfile)
            .SingleOrDefaultAsync(
                a => a.Id == customerId && a.AccountType == AccountType.Customer && a.CustomerProfile != null,
                cancellationToken);

    public Task<Account?> GetSelfProfileForUpdateAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        dbContext.Accounts
            .Include(a => a.CustomerProfile)
            .SingleOrDefaultAsync(
                a => a.Id == customerId && a.AccountType == AccountType.Customer && a.CustomerProfile != null,
                cancellationToken);

    public Task<bool> IsPhoneNumberTakenAsync(
        string phoneNumber,
        Guid excludeAccountId,
        CancellationToken cancellationToken = default) =>
        dbContext.Accounts
            .AnyAsync(
                a => a.PhoneNumber == phoneNumber && a.AccountType == AccountType.Customer && a.Id != excludeAccountId,
                cancellationToken);

    public Task<bool> HasPendingPayoutAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        dbContext.Payouts
            .AnyAsync(
                p => p.Status == PayoutStatus.Pending && p.ConsignmentContract.CustomerId == customerId,
                cancellationToken);
}
