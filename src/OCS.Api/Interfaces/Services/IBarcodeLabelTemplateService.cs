using OCS.Api.Common;
using OCS.Api.Models.Products;

namespace OCS.Api.Interfaces.Services;

public interface IBarcodeLabelTemplateService
{
    Task<BarcodeLabelTemplateDto> GetAsync(CancellationToken cancellationToken);
    Task<Result<BarcodeLabelTemplateDto>> UpdateAsync(BarcodeLabelTemplateDto request, CancellationToken cancellationToken);
}
