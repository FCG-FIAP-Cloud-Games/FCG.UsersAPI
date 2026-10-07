using FCG.Users.Domain.Entities;
using FCG.Users.Application.Abstractions.Repositories;

namespace FCG.Users.Application.Usuarios;

public sealed class ManipuladorAlterarPerfilUsuario
{
    private readonly IRepositoryUsuarios _repositorioUsuarios;
    private readonly TimeProvider _relogio;
    private readonly IUnidadeDeTrabalhoUsuarios _unidadeDeTrabalho;

    public ManipuladorAlterarPerfilUsuario(IRepositoryUsuarios repositorioUsuarios, TimeProvider relogio,
        IUnidadeDeTrabalhoUsuarios unidadeDeTrabalho)
    {
        _repositorioUsuarios = repositorioUsuarios;
        _relogio = relogio;
        _unidadeDeTrabalho = unidadeDeTrabalho;
    }

    public async Task<ResultadoAlterarPerfilUsuario> ProcessarAsync(
        ComandoAlterarPerfilUsuario comando,
        CancellationToken tokenCancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(comando);

        var erros = Validar(comando);
        if (erros.Count > 0)
            return ResultadoAlterarPerfilUsuario.DadosInvalidos(erros);

        return await _unidadeDeTrabalho.ExecutarAsync(comando.Id, async cancelamento =>
        {
            var usuario = await _repositorioUsuarios.ObterPorIdAsync(comando.Id, cancelamento);
            if (usuario is null)
                return ResultadoAlterarPerfilUsuario.NaoEncontrado();

            if (!await _repositorioUsuarios.PerfilExisteAsync(comando.PerfilId, cancelamento))
                return ResultadoAlterarPerfilUsuario.PerfilNaoEncontrado();

            if (usuario.PerfilId == comando.PerfilId)
                return ResultadoAlterarPerfilUsuario.Atualizado(DadosUsuario.De(usuario));
            usuario.AlterarPerfil(comando.PerfilId);
            var gravacao = await _repositorioUsuarios.AtualizarAsync(usuario, LogUsuario.RegistrarTrocaPerfil(usuario.Id, _relogio.GetUtcNow()), cancelamento);
            if (gravacao != ResultadoGravacaoUsuario.Sucesso)
                throw new InvalidOperationException("O repositório retornou um conflito inesperado ao alterar somente o perfil do usuário.");

            return ResultadoAlterarPerfilUsuario.Atualizado(DadosUsuario.De(usuario));
        }, tokenCancelamento);
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
