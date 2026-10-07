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
        await new UnidadeDeTrabalhoUsuarios(contexto).ExecutarAsync(token.UsuarioId, async cancelamento =>
        {
            contexto.Tokens.Add(token);
            await contexto.SaveChangesAsync(cancelamento);
            return 0;
        }, tokenCancelamento);
    }

    public Task<Token?> ObterPorHashAsync(string tokenHash, CancellationToken tokenCancelamento = default) =>
        contexto.Tokens.AsNoTracking().SingleOrDefaultAsync(token => token.TokenHash == tokenHash, tokenCancelamento);

    public Task<bool> TentarRotacionarAsync(string tokenHashAtual, Token novoToken,
        DateTimeOffset dataRevogacao, CancellationToken tokenCancelamento = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHashAtual);
        ArgumentNullException.ThrowIfNull(novoToken);
        ValidarDataUtc(dataRevogacao);
        return new UnidadeDeTrabalhoUsuarios(contexto).ExecutarAsync(novoToken.UsuarioId, async cancelamento =>
        {
            var quantidadeRevogada = await contexto.Tokens
                .Where(token => token.TokenHash == tokenHashAtual && token.UsuarioId == novoToken.UsuarioId
                    && token.DataRevogacao == null && token.DataExpiracao > dataRevogacao)
                .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.DataRevogacao, dataRevogacao), cancelamento);
            if (quantidadeRevogada != 1) return false;
            contexto.Tokens.Add(novoToken);
            await contexto.SaveChangesAsync(cancelamento);
            return true;
        }, tokenCancelamento);
    }

    public async Task RevogarTokensAtivosDoUsuarioAsync(Guid usuarioId, DateTimeOffset dataRevogacao,
        CancellationToken tokenCancelamento = default)
    {
        ValidarDataUtc(dataRevogacao);
        await new UnidadeDeTrabalhoUsuarios(contexto).ExecutarAsync(usuarioId, async cancelamento =>
            await contexto.Tokens.Where(token => token.UsuarioId == usuarioId
                && token.DataRevogacao == null && token.DataExpiracao > dataRevogacao)
                .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.DataRevogacao, dataRevogacao), cancelamento),
            tokenCancelamento);
    }

    private static void ValidarDataUtc(DateTimeOffset data)
    {
        if (data.Offset != TimeSpan.Zero)
            throw new ArgumentException("A data de revogação deve estar em UTC.", nameof(data));
    }
}
