using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mofang.Api.Security;
using Mofang.Application;
using Mofang.Contracts;

namespace Mofang.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/locations")]
public sealed class LocationsController(ILocationService locations) : ControllerBase
{
    [HttpGet("{kind}/{id:guid?}")]
    public Task<LocationDto> Get(string kind, Guid? id, CancellationToken cancellationToken) =>
        locations.GetAsync(kind, id, User.ToAccountContext(), cancellationToken);

    [HttpPost("resolve")]
    public Task<ResolveLocationResponse> Resolve(ResolveLocationRequest request, CancellationToken cancellationToken) =>
        locations.ResolveAsync(request.Input, User.ToAccountContext(), cancellationToken);
}
