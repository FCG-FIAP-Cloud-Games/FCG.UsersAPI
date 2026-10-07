using FCG.Users.Application.Abstractions.Repositories;
using FCG.Users.Domain.Entities;

namespace FCG.Users.Application.Usuarios;

public enum StatusInativacaoUsuario { Inativado, IdInvalido, NaoEncontrado }

public sealed class ManipuladorInativarUsuario(IRepositoryUsuarios repositorio, TimeProvider relogio,
    IUnidadeDeTrabalhoUsuarios unidadeDeTrabalho)
{
    public Task<StatusInativacaoUsuario> ProcessarAsync(ComandoInativarUsuario comando,
        CancellationToken tokenCancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(comando);
        if (comando.Id == Guid.Empty) return Task.FromResult(StatusInativacaoUsuario.IdInvalido);
        return unidadeDeTrabalho.ExecutarAsync(comando.Id, async cancelamento =>
        {
            var usuario = await repositorio.ObterPorIdAsync(comando.Id, cancelamento);
            if (usuario is null) return StatusInativacaoUsuario.NaoEncontrado;
            if (!usuario.Ativo) return StatusInativacaoUsuario.Inativado;
            var agora = relogio.GetUtcNow();
            usuario.Inativar(agora);
            var resultado = await repositorio.AtualizarAsync(usuario,
                LogUsuario.RegistrarInativacao(usuario.Id, agora), cancelamento);
            if (resultado != ResultadoGravacaoUsuario.Sucesso)
                throw new InvalidOperationException("A inativação retornou um conflito inesperado.");
            return StatusInativacaoUsuario.Inativado;
        }, tokenCancelamento);
    }
}
