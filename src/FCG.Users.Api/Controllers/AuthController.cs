using System.IdentityModel.Tokens.Jwt;
using FCG.Users.Api.Configuration;
using FCG.Users.Api.Contracts.Auth;
using FCG.Users.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FCG.Users.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Tags("Autenticação")]
[NaoArmazenarResposta]
public sealed class AuthController : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<RespostaLogin>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RespostaLogin>> LoginAsync(
        [FromBody] RequisicaoLogin requisicao,
        [FromServices] ManipuladorLogin manipulador,
        CancellationToken tokenCancelamento)
    {
        var resultado = await manipulador.ProcessarAsync(
            new ComandoLogin(requisicao.Email, requisicao.Senha),
            tokenCancelamento);

        if (resultado.Status == StatusLogin.DadosInvalidos)
        {
            return ResponderDadosInvalidos(resultado.Erros);
        }

        if (resultado.Status == StatusLogin.CredenciaisInvalidas)
        {
            // A resposta não informa se o e-mail existe, se a conta está inativa ou se a senha falhou.
            Response.Headers.WWWAuthenticate = "Bearer";
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Credenciais inválidas.",
                detail: "E-mail ou senha inválidos.");
        }

        if (resultado.Status != StatusLogin.Sucesso || resultado.Login is null)
            throw new InvalidOperationException("O login retornou um resultado inesperado.");

        return Ok(CriarResposta(resultado.Login));
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType<RespostaLogin>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RespostaLogin>> RefreshAsync(
        [FromBody] RequisicaoRefreshToken requisicao,
        [FromServices] ManipuladorRenovarToken manipulador,
        CancellationToken tokenCancelamento)
    {
        var resultado = await manipulador.ProcessarAsync(
            new ComandoRenovarToken(requisicao.RefreshToken), tokenCancelamento);

        if (resultado.Status == StatusRenovacaoToken.DadosInvalidos)
            return ResponderDadosInvalidos(resultado.Erros);

        if (resultado.Status == StatusRenovacaoToken.TokenInvalido)
        {
            // A mesma resposta cobre token desconhecido, vencido, já usado e usuário inativo.
            Response.Headers.WWWAuthenticate = "Bearer";
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Renovação não autorizada.",
                detail: "Não foi possível renovar a sessão. Faça login novamente.");
        }

        if (resultado.Status != StatusRenovacaoToken.Sucesso || resultado.Login is null)
            throw new InvalidOperationException("A renovação retornou um resultado inesperado.");

        return Ok(CriarResposta(resultado.Login));
    }

    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> LogoutAsync(
        [FromServices] ManipuladorLogout manipulador,
        CancellationToken tokenCancelamento)
    {
        // O middleware já validou sub, assinatura e prazo. Nenhum ID é aceito do cliente.
        var usuarioId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        await manipulador.ProcessarAsync(usuarioId, tokenCancelamento);
        return NoContent();
    }

    private ActionResult ResponderDadosInvalidos(IReadOnlyDictionary<string, string[]> errosInformados)
    {
        var erros = new ModelStateDictionary();
        foreach (var (campo, mensagens) in errosInformados)
            foreach (var mensagem in mensagens)
                erros.AddModelError(campo, mensagem);

        return ValidationProblem(
            modelStateDictionary: erros,
            title: "Um ou mais dados informados são inválidos.");
    }

    private static RespostaLogin CriarResposta(LoginRealizado login) => new()
    {
        AccessToken = login.AccessToken,
        RefreshToken = login.RefreshToken,
        TokenType = login.TokenType,
        ExpiresIn = login.ExpiresIn,
        ExpiresAt = login.ExpiresAt,
        Usuario = new RespostaUsuarioLogado(
            login.Usuario.Id, login.Usuario.Nome, login.Usuario.Email,
            login.Usuario.PerfilId, login.Usuario.Perfil)
    };
}
