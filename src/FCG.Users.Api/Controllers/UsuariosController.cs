using FCG.Users.Api.Contracts.Usuarios;
using FCG.Users.Application.Usuarios;
using FCG.Users.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FCG.Users.Api.Controllers;

[ApiController]
[Route("api/v1/usuarios")]
[Tags("Usuários")]
public sealed class UsuariosController : ControllerBase
{
    [AllowAnonymous]
    [HttpPost]
    [ProducesResponseType<RespostaUsuario>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RespostaUsuario>> CriarAsync(
        [FromBody] RequisicaoCriarUsuario requisicao,
        [FromServices] ManipuladorCriarUsuario manipulador,
        CancellationToken tokenCancelamento)
    {
        var resultado = await manipulador.ProcessarAsync(
            new ComandoCriarUsuario(
                requisicao.Nome,
                requisicao.CPF,
                requisicao.DataNascimento,
                requisicao.Email,
                requisicao.Senha,
                PerfisSistema.UsuarioId),
            tokenCancelamento);

        if (resultado.Status == StatusCriacaoUsuario.DadosInvalidos)
        {
            var erros = new ModelStateDictionary();
            foreach (var (campo, mensagens) in resultado.Erros)
            {
                foreach (var mensagem in mensagens)
                    erros.AddModelError(campo, mensagem);
            }

            return ValidationProblem(
                modelStateDictionary: erros,
                title: "Um ou mais dados informados são inválidos.");
        }

        if (resultado.Status == StatusCriacaoUsuario.PerfilNaoEncontrado)
        {
            // O cliente não escolhe esse perfil. Sua ausência é uma falha de configuração.
            throw new InvalidOperationException("O perfil padrão para cadastro não está configurado.");
        }

        if (resultado.Status is StatusCriacaoUsuario.EmailJaCadastrado or StatusCriacaoUsuario.CpfJaCadastrado)
        {
            var campo = resultado.Status == StatusCriacaoUsuario.EmailJaCadastrado ? "e-mail" : "CPF";
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"{campo} já cadastrado.",
                detail: $"Já existe um usuário cadastrado com o {campo} informado.");
        }

        if (resultado.Status != StatusCriacaoUsuario.Criado || resultado.Usuario is null)
            throw new InvalidOperationException("O cadastro retornou um resultado inesperado.");

        var usuario = resultado.Usuario;
        var resposta = new RespostaUsuario(
            usuario.Id,
            usuario.Nome,
            usuario.Email,
            usuario.PerfilId,
            usuario.Ativo,
            usuario.CriadoEmUtc,
            usuario.DataInativacao);

        // Mantém o endereço do recurso. Sua consulta por GET será conectada na E09.
        return Created($"/api/v1/usuarios/{resposta.Id}", resposta);
    }
}
