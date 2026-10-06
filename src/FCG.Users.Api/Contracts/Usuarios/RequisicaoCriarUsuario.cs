namespace FCG.Users.Api.Contracts.Usuarios;

// O perfil não faz parte da entrada pública: o servidor sempre escolhe Usuario.
public sealed class RequisicaoCriarUsuario
{
    public string Nome { get; init; } = string.Empty;
    public string CPF { get; init; } = string.Empty;
    public DateOnly DataNascimento { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Senha { get; init; } = string.Empty;
}
