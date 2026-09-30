using FCG.Users.Domain.Entities;

namespace FCG.Users.Application.Abstractions.Repositories;

public interface IRepositoryUsuarios
{
    Task<Usuario?> ObterPorIdAsync(Guid id, CancellationToken tokenCancelamento = default);
    Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken tokenCancelamento = default);
    Task<UsuarioAutenticacao?> ObterAutenticacaoPorEmailAsync(
        string email,
        CancellationToken tokenCancelamento = default);
    Task<UsuarioAutenticacao?> ObterAutenticacaoPorIdAsync(
        Guid id,
        CancellationToken tokenCancelamento = default);
    Task<bool> ExisteEmailAsync(string email, Guid? ignorarUsuarioId, CancellationToken tokenCancelamento = default);
    Task<bool> ExisteCpfAsync(string cpf, Guid? ignorarUsuarioId, CancellationToken tokenCancelamento = default);
    Task<bool> PerfilExisteAsync(Guid perfilId, CancellationToken tokenCancelamento = default);
    Task<bool> TentarAdicionarAsync(Usuario usuario, CancellationToken tokenCancelamento = default);
    Task AtualizarAsync(Usuario usuario, CancellationToken tokenCancelamento = default);
}

public sealed record UsuarioAutenticacao(Usuario Usuario, string Perfil);
