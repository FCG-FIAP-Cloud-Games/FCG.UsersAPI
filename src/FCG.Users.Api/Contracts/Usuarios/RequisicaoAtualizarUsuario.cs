namespace FCG.Users.Api.Contracts.Usuarios;

public sealed class RequisicaoAtualizarUsuario
{
    public string Nome { get; init; } = string.Empty;
    public DateOnly DataNascimento { get; init; }
    public string Email { get; init; } = string.Empty;
}
