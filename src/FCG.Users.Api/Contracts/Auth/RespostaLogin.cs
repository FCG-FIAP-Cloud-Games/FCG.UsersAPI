namespace FCG.Users.Api.Contracts.Auth;

public sealed record RespostaUsuarioLogado(
    Guid Id,
    string Nome,
    string Email,
    Guid PerfilId,
    string Perfil);

// Os tokens fazem parte da resposta HTTP, mas não de uma representação automática em texto.
public sealed class RespostaLogin
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required string TokenType { get; init; }
    public long ExpiresIn { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public required RespostaUsuarioLogado Usuario { get; init; }
}
