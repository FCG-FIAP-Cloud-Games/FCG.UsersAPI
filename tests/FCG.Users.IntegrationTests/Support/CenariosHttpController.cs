using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;

namespace FCG.Users.IntegrationTests.Support;

// Carregado somente pela fábrica de testes que solicita estes cenários.
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("__testes/e03")]
public sealed class CenariosHttpController : ControllerBase
{
    [HttpGet("falha")]
    [SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Uma ação de controller deve ser um método de instância para ser descoberta pelo MVC.")]
    public IActionResult Falhar() =>
        throw new InvalidOperationException("detalhe-interno-simulado-e03");

    [HttpPost("validacao")]
    public IActionResult Validar([FromBody] RequisicaoTeste requisicao) => Ok(requisicao);
}

public sealed record RequisicaoTeste
{
    [Required]
    public string? Nome { get; init; }
}
