using FCG.Users.Api.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FCG.Users.Api.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    private readonly HealthCheckService _healthChecks;

    public HealthController(HealthCheckService healthChecks)
    {
        _healthChecks = healthChecks;
    }

    [HttpGet]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthResponse>> VerificarAsync(CancellationToken tokenCancelamento)
    {
        var relatorio = await _healthChecks.CheckHealthAsync(tokenCancelamento);
        var statusHttp = relatorio.Status == HealthStatus.Unhealthy
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;

        return StatusCode(statusHttp, new HealthResponse(relatorio.Status.ToString()));
    }
}
