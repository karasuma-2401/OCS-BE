using Microsoft.EntityFrameworkCore;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Models.Customers;

namespace OCS.Api.Services;

public sealed class CustomerConsignmentService(AppDbContext db, ICustomerProfileRepository customerProfiles)
{
    public async Task<ConsignmentTrackingResponse[]?> GetAsync(Guid customerId, CancellationToken cancellationToken)
    {
        // Account status, email verification and current roles are validated by JWT authentication.
        var account = await customerProfiles.GetSelfProfileAsync(customerId, cancellationToken);
        if (account?.CustomerProfile?.Status != CustomerStatus.Active) return null;

        var contracts = await db.ConsignmentContracts.AsNoTracking()
            .Where(c => c.CustomerId == customerId)
            .Include(c => c.Products)
            .Include(c => c.Payouts)
            .AsSplitQuery()
            .OrderByDescending(c => c.SignedAt).ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

        return contracts.Select(c =>
        {
            var products = c.Products.OrderBy(p => p.Name).ThenBy(p => p.Id).Select(p =>
                new ConsignmentProductResponse(p.Id, p.Barcode, p.Name, p.Status.ToString(), p.ListedPrice,
                    p.AgreedCommissionRate,
                    p.AgreedCommissionRate is null ? null : p.CalculateEstimatedConsignorPayout())).ToArray();
            var estimatesComplete = products.All(p => p.EstimatedPayout.HasValue);
            return new ConsignmentTrackingResponse(c.Id, c.ContractNumber, c.AppointmentId, c.SignedAt,
                c.EndsAt, c.Status.ToString(), c.ExpiryHandling.ToString(), products,
                estimatesComplete ? products.Sum(p => p.EstimatedPayout!.Value) : null,
                c.Payouts.Where(p => p.Status == PayoutStatus.Completed).Sum(p => p.TotalAmount));
        }).ToArray();
    }
}
