using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mofang.Api.Security;
using Mofang.Application;
using Mofang.Contracts;

namespace Mofang.Api.Controllers;

[ApiController]
[Authorize(Policy = MofangPolicies.MasterAdmin)]
[Route("api/admin/storage/minio")]
public sealed class StorageAdministrationController(IMinioAdministrationService minio) : ControllerBase
{
    [HttpGet]
    public Task<MinioSettingsDto> Get(CancellationToken cancellationToken) => minio.GetAsync(cancellationToken);

    [HttpPost("test")]
    public Task<MinioConnectionTestDto> Test(UpdateMinioSettingsRequest request, CancellationToken cancellationToken) =>
        minio.TestAsync(request, cancellationToken);

    [HttpPut]
    public Task<MinioSettingsDto> Update(UpdateMinioSettingsRequest request, CancellationToken cancellationToken) =>
        minio.UpdateAsync(request, User.ToAccountContext(), cancellationToken);
}
