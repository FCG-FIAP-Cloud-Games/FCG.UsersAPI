using FCG.Users.Application.Abstractions.Repositories;
using FCG.Users.Domain.Entities;
using FCG.Users.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FCG.Users.Infrastructure.Repositories;

public sealed class RepositorioTokens(UsersDbContext contexto) : IRepositorioTokens
{
    public async Task AdicionarAsync(Token token, CancellationToken tokenCancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        await using var transacao = await contexto.Database.BeginTransactionAsync(tokenCancelamento);
        await BloquearSessoesDoUsuarioAsync(token.UsuarioId, tokenCancelamento);
        contexto.Tokens.Add(token);
        await contexto.SaveChangesAsync(tokenCancelamento);
        await transacao.CommitAsync(tokenCancelamento);
    }

    public Task<Token?> ObterPorHashAsync(
        string tokenHash,
        CancellationToken tokenCancelamento = default) =>
        contexto.Tokens.AsNoTracking().SingleOrDefaultAsync(token => token.TokenHash == tokenHash, tokenCancelamento);

    public async Task<bool> TentarRotacionarAsync(
        string tokenHashAtual,
        Token novoToken,
        DateTimeOffset dataRevogacao,
        CancellationToken tokenCancelamento = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHashAtual);
        ArgumentNullException.ThrowIfNull(novoToken);
        ValidarDataUtc(dataRevogacao);

        await using var transacao = await contexto.Database.BeginTransactionAsync(tokenCancelamento);
        try
        {
            await BloquearSessoesDoUsuarioAsync(novoToken.UsuarioId, tokenCancelamento);

            // A condição é reavaliada pelo PostgreSQL após obter o bloqueio da linha:
            // entre duas renovações simultâneas, apenas uma consegue revogar o token atual.
            var quantidadeRevogada = await contexto.Tokens
                .Where(token => token.TokenHash == tokenHashAtual
                    && token.UsuarioId == novoToken.UsuarioId
                    && token.DataRevogacao == null
                    && token.DataExpiracao > dataRevogacao)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(token => token.DataRevogacao, dataRevogacao),
                    tokenCancelamento);

            if (quantidadeRevogada != 1)
            {
                await transacao.RollbackAsync(tokenCancelamento);
                return false;
            }

            contexto.Tokens.Add(novoToken);
            await contexto.SaveChangesAsync(tokenCancelamento);
            await transacao.CommitAsync(tokenCancelamento);
            return true;
        }
        catch
        {
            // O dispose da transação reverte a revogação caso a inserção/commit falhe.
            // Remover a entidade pendente impede uma inserção acidental em SaveChanges posterior.
            contexto.Entry(novoToken).State = EntityState.Detached;
            throw;
        }
    }

    public async Task RevogarTokensAtivosDoUsuarioAsync(
        Guid usuarioId,
        DateTimeOffset dataRevogacao,
        CancellationToken tokenCancelamento = default)
    {
        ValidarDataUtc(dataRevogacao);
        await using var transacao = await contexto.Database.BeginTransactionAsync(tokenCancelamento);
        await BloquearSessoesDoUsuarioAsync(usuarioId, tokenCancelamento);

        await contexto.Tokens
            .Where(token => token.UsuarioId == usuarioId
                && token.DataRevogacao == null
                && token.DataExpiracao > dataRevogacao)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.DataRevogacao, dataRevogacao),
                tokenCancelamento);

        await transacao.CommitAsync(tokenCancelamento);
    }

    private Task<int> BloquearSessoesDoUsuarioAsync(Guid usuarioId, CancellationToken tokenCancelamento) =>
        // O mesmo bloqueio é obtido por login, refresh e logout e liberado no fim da transação.
        // O logout consulta as sessões DEPOIS do bloqueio: vê o sucessor de um refresh anterior.
        // Um refresh posterior ao logout encontra o token antigo revogado. O SQL é parametrizado.
        contexto.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"tb_Usuarios\" WHERE \"Id\" = {usuarioId} FOR UPDATE",
            tokenCancelamento);

    private static void ValidarDataUtc(DateTimeOffset data)
    {
        if (data.Offset != TimeSpan.Zero)
            throw new ArgumentException("A data de revogação deve estar em UTC.", nameof(data));
    }
}
