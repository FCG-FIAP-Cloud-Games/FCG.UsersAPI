using FCG.Users.Application.Abstractions.Repositories;
using FCG.Users.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FCG.Users.Infrastructure.Repositories;

public sealed class UnidadeDeTrabalhoUsuarios(UsersDbContext contexto) : IUnidadeDeTrabalhoUsuarios
{
    public async Task<T> ExecutarAsync<T>(Guid usuarioId, Func<CancellationToken, Task<T>> operacao,
        CancellationToken tokenCancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(operacao);
        if (usuarioId == Guid.Empty) throw new ArgumentException("O usuário é obrigatório.", nameof(usuarioId));

        // Chamadas internas participam da transação já aberta na mesma unidade de trabalho.
        if (contexto.Database.CurrentTransaction is not null)
        {
            await BloquearAsync(usuarioId, tokenCancelamento);
            return await operacao(tokenCancelamento);
        }

        await using var transacao = await contexto.Database.BeginTransactionAsync(tokenCancelamento);
        try
        {
            await BloquearAsync(usuarioId, tokenCancelamento);
            var resultado = await operacao(tokenCancelamento);
            await transacao.CommitAsync(tokenCancelamento);
            return resultado;
        }
        catch
        {
            // Dispose desfaz a transação. Limpar evita reutilizar estado de uma gravação revertida.
            contexto.ChangeTracker.Clear();
            throw;
        }
    }

    private Task<int> BloquearAsync(Guid usuarioId, CancellationToken tokenCancelamento) =>
        contexto.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"tb_Usuarios\" WHERE \"Id\" = {usuarioId} FOR UPDATE", tokenCancelamento);
}
