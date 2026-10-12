namespace FCG.Users.Api.Contracts.Auth;

// Classe comum para evitar que ToString() inclua a senha, como ocorreria em um record.
public sealed class RequisicaoLogin
{
    public string Email { get; init; } = string.Empty;
    public string Senha { get; init; } = string.Empty;
}
