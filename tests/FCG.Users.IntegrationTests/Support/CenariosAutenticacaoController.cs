using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FCG.Users.IntegrationTests.Support;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("__testes/e07")]
public sealed class CenariosAutenticacaoController : ControllerBase
{
    [Authorize]
    [HttpGet("autenticado")]
    public IActionResult Autenticado() => Ok(new
    {
        sub = User.FindFirst("sub")?.Value,
        role = User.FindFirst("role")?.Value,
        nomeIdentidade = User.Identity?.Name
    });

    [Authorize(Roles = "Administrador")]
    [HttpGet("administrador")]
    public IActionResult Administrador() => Ok(new { autorizado = User.IsInRole("Administrador") });
}
