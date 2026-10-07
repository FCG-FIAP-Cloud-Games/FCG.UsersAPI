using FCG.Users.Api.Configuration;
using FCG.Users.Api.Contracts.Usuarios;
using FCG.Users.Application.Usuarios;
using FCG.Users.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FCG.Users.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/usuarios")]
[Tags("Usuários")]
[NaoArmazenarResposta]
public sealed class UsuariosController(IAuthorizationService autorizacao) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost]
    [ProducesResponseType<RespostaUsuario>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public Task<ActionResult<RespostaUsuario>> CriarAsync([FromBody] RequisicaoCriarUsuario requisicao,
        [FromServices] ManipuladorCriarUsuario manipulador, CancellationToken tokenCancelamento) =>
        CriarComPerfilAsync(requisicao, PerfisSistema.UsuarioId, manipulador, tokenCancelamento);

    [Authorize(Roles = PerfisSistema.Administrador)]
    [HttpPost("administradores")]
    [ProducesResponseType<RespostaUsuario>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public Task<ActionResult<RespostaUsuario>> CriarAdministradorAsync([FromBody] RequisicaoCriarUsuario requisicao,
        [FromServices] ManipuladorCriarUsuario manipulador, CancellationToken tokenCancelamento) =>
        CriarComPerfilAsync(requisicao, PerfisSistema.AdministradorId, manipulador, tokenCancelamento);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<RespostaUsuario>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RespostaUsuario>> ObterAsync(Guid id,
        [FromServices] ManipuladorObterUsuario manipulador, CancellationToken tokenCancelamento)
    {
        if (!(await autorizacao.AuthorizeAsync(User, id, PoliticasUsuarios.TitularOuAdministrador)).Succeeded)
            return Forbid();
        var resultado = await manipulador.ProcessarAsync(new ConsultaObterUsuario(id), tokenCancelamento);
        if (resultado.Status == StatusObtencaoUsuario.IdInvalido) return ResponderIdInvalido();
        if (resultado.Status == StatusObtencaoUsuario.NaoEncontrado) return ResponderNaoEncontrado();
        if (resultado.Usuario is null) throw new InvalidOperationException("Consulta sem usuário no resultado.");
        return Ok(CriarResposta(resultado.Usuario));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<RespostaUsuario>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RespostaUsuario>> AtualizarAsync(Guid id,
        [FromBody] RequisicaoAtualizarUsuario requisicao, [FromServices] ManipuladorAtualizarUsuario manipulador,
        CancellationToken tokenCancelamento)
    {
        if (!(await autorizacao.AuthorizeAsync(User, id, PoliticasUsuarios.TitularOuAdministrador)).Succeeded)
            return Forbid();
        var resultado = await manipulador.ProcessarAsync(new ComandoAtualizarUsuario(
            id, requisicao.Nome, requisicao.DataNascimento, requisicao.Email), tokenCancelamento);
        if (resultado.Status == StatusAtualizacaoUsuario.DadosInvalidos) return ResponderDadosInvalidos(resultado.Erros);
        if (resultado.Status == StatusAtualizacaoUsuario.NaoEncontrado) return ResponderNaoEncontrado();
        if (resultado.Status == StatusAtualizacaoUsuario.EmailJaCadastrado)
            return Problem(statusCode: 409, title: "E-mail já cadastrado.", detail: "Já existe outro usuário cadastrado com o e-mail informado.");
        if (resultado.Usuario is null) throw new InvalidOperationException("Atualização sem usuário no resultado.");
        return Ok(CriarResposta(resultado.Usuario));
    }

    [Authorize(Roles = PerfisSistema.Administrador)]
    [HttpPut("{id:guid}/perfil")]
    [ProducesResponseType<RespostaUsuario>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RespostaUsuario>> AlterarPerfilAsync(Guid id,
        [FromBody] RequisicaoAlterarPerfilUsuario requisicao, [FromServices] ManipuladorAlterarPerfilUsuario manipulador,
        CancellationToken tokenCancelamento)
    {
        var resultado = await manipulador.ProcessarAsync(new ComandoAlterarPerfilUsuario(id, requisicao.PerfilId), tokenCancelamento);
        if (resultado.Status == StatusAlteracaoPerfilUsuario.DadosInvalidos) return ResponderDadosInvalidos(resultado.Erros);
        if (resultado.Status == StatusAlteracaoPerfilUsuario.PerfilNaoEncontrado)
            return ResponderDadosInvalidos(new Dictionary<string, string[]> { ["perfilId"] = ["O perfil informado não existe."] });
        if (resultado.Status == StatusAlteracaoPerfilUsuario.NaoEncontrado) return ResponderNaoEncontrado();
        if (resultado.Usuario is null) throw new InvalidOperationException("Troca de perfil sem usuário no resultado.");
        return Ok(CriarResposta(resultado.Usuario));
    }

    [Authorize(Roles = PerfisSistema.Administrador)]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> InativarAsync(Guid id, [FromServices] ManipuladorInativarUsuario manipulador,
        CancellationToken tokenCancelamento)
    {
        var resultado = await manipulador.ProcessarAsync(new ComandoInativarUsuario(id), tokenCancelamento);
        return resultado switch
        {
            StatusInativacaoUsuario.IdInvalido => ResponderIdInvalido(),
            StatusInativacaoUsuario.NaoEncontrado => ResponderNaoEncontrado(),
            StatusInativacaoUsuario.Inativado => NoContent(),
            _ => throw new InvalidOperationException("Resultado de inativação inesperado.")
        };
    }

    private async Task<ActionResult<RespostaUsuario>> CriarComPerfilAsync(RequisicaoCriarUsuario requisicao,
        Guid perfilId, ManipuladorCriarUsuario manipulador, CancellationToken tokenCancelamento)
    {
        var resultado = await manipulador.ProcessarAsync(new ComandoCriarUsuario(
            requisicao.Nome, requisicao.CPF, requisicao.DataNascimento, requisicao.Email, requisicao.Senha, perfilId), tokenCancelamento);
        if (resultado.Status == StatusCriacaoUsuario.DadosInvalidos) return ResponderDadosInvalidos(resultado.Erros);
        if (resultado.Status == StatusCriacaoUsuario.PerfilNaoEncontrado)
            throw new InvalidOperationException("O perfil necessário para cadastro não está configurado.");
        if (resultado.Status is StatusCriacaoUsuario.EmailJaCadastrado or StatusCriacaoUsuario.CpfJaCadastrado)
        {
            var campo = resultado.Status == StatusCriacaoUsuario.EmailJaCadastrado ? "e-mail" : "CPF";
            return Problem(statusCode: 409, title: $"{campo} já cadastrado.", detail: $"Já existe um usuário cadastrado com o {campo} informado.");
        }
        if (resultado.Status != StatusCriacaoUsuario.Criado || resultado.Usuario is null)
            throw new InvalidOperationException("O cadastro retornou um resultado inesperado.");
        var resposta = CriarResposta(resultado.Usuario);
        return Created($"/api/v1/usuarios/{resposta.Id}", resposta);
    }

    private ActionResult ResponderDadosInvalidos(IReadOnlyDictionary<string, string[]> errosInformados)
    {
        var erros = new ModelStateDictionary();
        foreach (var (campo, mensagens) in errosInformados)
            foreach (var mensagem in mensagens) erros.AddModelError(campo, mensagem);
        return ValidationProblem(modelStateDictionary: erros, title: "Um ou mais dados informados são inválidos.");
    }
    private ActionResult ResponderIdInvalido() => ResponderDadosInvalidos(
        new Dictionary<string, string[]> { ["id"] = ["Informe um identificador de usuário válido."] });
    private ObjectResult ResponderNaoEncontrado() => Problem(statusCode: 404,
        title: "Usuário não encontrado.", detail: "Não existe usuário com o identificador informado.");
    private static RespostaUsuario CriarResposta(DadosUsuario usuario) => new(usuario.Id, usuario.Nome, usuario.Email,
        usuario.PerfilId, usuario.Ativo, usuario.CriadoEmUtc, usuario.DataInativacao);
}
