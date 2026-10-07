namespace FCG.Users.Application.Abstractions.Repositories;

public interface IUnidadeDeTrabalhoUsuarios
{
    // Serializa por usuário. Quem abre a transação confirma antes de devolver o resultado;
    // chamadas internas participam da transação existente sem commit independente.
    Task<T> ExecutarAsync<T>(Guid usuarioId, Func<CancellationToken, Task<T>> operacao,
        CancellationToken tokenCancelamento = default);
}
