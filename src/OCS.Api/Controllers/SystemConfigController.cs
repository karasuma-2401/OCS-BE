using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Authorization;
using OCS.Api.Extensions;
using OCS.Api.Models.SystemConfig;
using OCS.Api.Services;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/system-config")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SystemConfigController(SystemConfigService systemConfig) : ControllerBase
{
    [HttpGet("defaults")]
    [Authorize(Policy = PermissionCatalog.AppointmentsStaffReception)]
    public async Task<IActionResult> GetDefaults(CancellationToken ct) =>
        (await systemConfig.GetReceptionDefaultsAsync(ct)).ToActionResult(this);

    [HttpGet]
    [Authorize(Policy = PermissionCatalog.SettingsRead)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        (await systemConfig.GetAsync(ct)).ToActionResult(this);

    [HttpPut]
    [Authorize(Policy = PermissionCatalog.SettingsUpdate)]
    public async Task<IActionResult> Update([FromBody] UpdateSystemConfigRequest request, CancellationToken ct) =>
        (await systemConfig.UpdateAsync(request, ct)).ToActionResult(this);
}
