using OCS.Api.Common;
using OCS.Api.Models.Pos;

namespace OCS.Api.Interfaces.Services;

public interface IPosService
{
    Task<Result<PosProductLookupDto>> LookupPosProductAsync(
        string identifier, CancellationToken cancellationToken = default);
   
    Task<Result<PosCashPaymentResponse>> CheckoutCashAsync(
        Guid actorAccountId, 
        PosCashPaymentRequest request, 
        CancellationToken cancellationToken = default);

    Task<Result<PosInvoiceDetailDto>> GetInvoiceDetailAsync(
        string invoiceIdOrNumber,
        CancellationToken cancellationToken = default);

    Task<Result<byte[]>> ExportInvoicePdfAsync(
        string invoiceIdOrNumber,
        CancellationToken cancellationToken = default);
}