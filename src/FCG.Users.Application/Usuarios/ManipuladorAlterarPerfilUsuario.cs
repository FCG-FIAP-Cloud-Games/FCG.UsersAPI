using FCG.Users.Application.Abstractions.Repositories;

namespace FCG.Users.Application.Usuarios;

public sealed class ManipuladorAlterarPerfilUsuario
{
    private readonly IRepositoryUsuarios _repositorioUsuarios;

    public ManipuladorAlterarPerfilUsuario(IRepositoryUsuarios repositorioUsuarios)
    {
        _repositorioUsuarios = repositorioUsuarios;
    }

    public async Task<ResultadoAlterarPerfilUsuario> ProcessarAsync(
        ComandoAlterarPerfilUsuario comando,
        CancellationToken tokenCancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(comando);

        var erros = Validar(comando);
        if (erros.Count > 0)
            return ResultadoAlterarPerfilUsuario.DadosInvalidos(erros);

        var usuario = await _repositorioUsuarios.ObterPorIdAsync(comando.Id, tokenCancelamento);
        if (usuario is null)
            return ResultadoAlterarPerfilUsuario.NaoEncontrado();

        if (!await _repositorioUsuarios.PerfilExisteAsync(comando.PerfilId, tokenCancelamento))
            return ResultadoAlterarPerfilUsuario.PerfilNaoEncontrado();

        usuario.AlterarPerfil(comando.PerfilId);
        var gravacao = await _repositorioUsuarios.AtualizarAsync(usuario, tokenCancelamento);
        if (gravacao != ResultadoGravacaoUsuario.Sucesso)
            throw new InvalidOperationException("O repositório retornou um conflito inesperado ao alterar somente o perfil do usuário.");

        return ResultadoAlterarPerfilUsuario.Atualizado(DadosUsuario.De(usuario));
    }

    private static Dictionary<string, string[]> Validar(ComandoAlterarPerfilUsuario comando)
    {
        var erros = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (comando.Id == Guid.Empty)
            erros["id"] = ["Informe um identificador de usuário válido."];

        if (comando.PerfilId == Guid.Empty)
            erros["perfilId"] = ["Informe um perfil válido."];

        return erros;
    }
}
