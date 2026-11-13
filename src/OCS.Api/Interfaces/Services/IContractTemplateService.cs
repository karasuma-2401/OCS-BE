using OCS.Api.Common;
using OCS.Api.Models.Contracts;

namespace OCS.Api.Interfaces.Services;

public interface IContractTemplateService
{
    Task<ContractTemplateResponse> GetAsync(CancellationToken cancellationToken);
    Task<Result<ContractTemplateResponse>> UpdateAsync(UpdateContractTemplateRequest request, Guid actorId, CancellationToken cancellationToken);
    Result<byte[]> Preview(UpdateContractTemplateRequest request);
}
