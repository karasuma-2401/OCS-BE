using OCS.Api.Common;
using OCS.Api.Models.Contracts;
using OCS.Api.Models.Products;

namespace OCS.Api.Interfaces.Services;

public interface IContractService
{
    Task<Result<ContractResponse>> CreateAsync(CreateContractRequest request, Guid employeeId, CancellationToken cancellationToken);
    Task<Result<byte[]>> PreviewAsync(CreateContractRequest request, Guid employeeId, CancellationToken cancellationToken);
    Task<Result<ContractResponse>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<byte[]?> GetPdfAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContractListItem>> ListAsync(int page, CancellationToken cancellationToken);
    Task<Result<ContractCustomerResponse>> FindCustomerByPhoneAsync(string phone, CancellationToken cancellationToken);
    Task<Result<ContractCustomerResponse>> GetCustomerAsync(Guid id, CancellationToken cancellationToken);
    Task<ContractOptionsResponse> GetOptionsAsync(CancellationToken cancellationToken);
    Task<Result<ContractEmployeeResponse>> GetEmployeeAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<ContractCustomerResponse>> UpdateCustomerAsync(Guid id, UpdateContractCustomerRequest request, CancellationToken cancellationToken);
    Task<ContractImageContent?> GetImageAsync(Guid id, CancellationToken cancellationToken);
    Task<string> GenerateBarcodeAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<BarcodeLabelProduct>?> GetBarcodeProductsAsync(Guid id, CancellationToken cancellationToken);
}
