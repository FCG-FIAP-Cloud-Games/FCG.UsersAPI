namespace FCG.Users.Api.Contracts.Auth;

// Classe comum: a representação automática em texto não revela a credencial.
public sealed class RequisicaoRefreshToken
{
    public string RefreshToken { get; init; } = string.Empty;
}
