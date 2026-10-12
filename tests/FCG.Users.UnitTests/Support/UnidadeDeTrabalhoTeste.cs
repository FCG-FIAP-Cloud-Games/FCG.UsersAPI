using FCG.Users.Application.Abstractions.Repositories;

namespace FCG.Users.UnitTests.Support;

// Os testes de integração comprovam transação/bloqueio; aqui verificamos a coordenação do caso de uso.
internal sealed class UnidadeDeTrabalhoTeste : IUnidadeDeTrabalhoUsuarios
{
    public Task<T> ExecutarAsync<T>(Guid usuarioId, Func<CancellationToken, Task<T>> operacao,
        CancellationToken tokenCancelamento = default) => operacao(tokenCancelamento);
}
