using FCG.Users.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FCG.Users.Api.Controllers;

[ApiController]
[AllowAnonymous]
public sealed class JwksController(ChavesJwtRsa chaves) : ControllerBase
{
    [HttpGet("/.well-known/jwks.json")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    public IActionResult Obter() => Ok(chaves.CriarJwks());
}
