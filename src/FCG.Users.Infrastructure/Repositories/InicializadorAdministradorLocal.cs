using FCG.Users.Application.Usuarios;
using FCG.Users.Domain.Entities;
using FCG.Users.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FCG.Users.Infrastructure.Repositories;

public enum StatusInicializacaoAdministrador { Criado, JaExistente, DadosInvalidos, Conflito }

public sealed class InicializadorAdministradorLocal(UsersDbContext contexto, ManipuladorCriarUsuario cadastro)
{
    public async Task<StatusInicializacaoAdministrador> ExecutarAsync(ComandoCriarUsuario comando,
        CancellationToken tokenCancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(comando);
        if (comando.PerfilId != PerfisSistema.AdministradorId)
            throw new ArgumentException("O inicializador aceita somente o perfil administrativo.", nameof(comando));
        await using var transacao = await contexto.Database.BeginTransactionAsync(tokenCancelamento);
        // Coordena duas inicializações simultâneas antes de qualquer usuário existir.
        await contexto.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"tb_Perfil\" WHERE \"Id\" = {PerfisSistema.AdministradorId} FOR UPDATE", tokenCancelamento);
        if (await contexto.Usuarios.AnyAsync(usuario => usuario.PerfilId == PerfisSistema.AdministradorId, tokenCancelamento))
            return StatusInicializacaoAdministrador.JaExistente;
        var resultado = await cadastro.ProcessarAsync(comando, tokenCancelamento);
        if (resultado.Status == StatusCriacaoUsuario.DadosInvalidos || resultado.Status == StatusCriacaoUsuario.PerfilNaoEncontrado)
            return StatusInicializacaoAdministrador.DadosInvalidos;
        if (resultado.Status != StatusCriacaoUsuario.Criado)
            return StatusInicializacaoAdministrador.Conflito;
        await transacao.CommitAsync(tokenCancelamento);
        return StatusInicializacaoAdministrador.Criado;
    }
}
