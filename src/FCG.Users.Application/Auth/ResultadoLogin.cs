namespace FCG.Users.Application.Auth;

public enum StatusLogin
{
    Sucesso,
    DadosInvalidos,
    CredenciaisInvalidas
}

public sealed record UsuarioLogado(
    Guid Id,
    string Nome,
    string Email,
    Guid PerfilId,
    string Perfil);

public sealed record LoginRealizado(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    long ExpiresIn,
    DateTimeOffset ExpiresAt,
    UsuarioLogado Usuario);

public sealed class ResultadoLogin
{
    private ResultadoLogin(
        StatusLogin status,
        LoginRealizado? login,
        IReadOnlyDictionary<string, string[]>? erros)
    {
        Status = status;
        Login = login;
        Erros = erros ?? new Dictionary<string, string[]>();
    }

    public StatusLogin Status { get; }
    public LoginRealizado? Login { get; }
    public IReadOnlyDictionary<string, string[]> Erros { get; }

    public static ResultadoLogin Autenticado(LoginRealizado login) =>
        new(StatusLogin.Sucesso, login, null);

    public static ResultadoLogin DadosInvalidos(IReadOnlyDictionary<string, string[]> erros) =>
        new(StatusLogin.DadosInvalidos, null, erros);

    public static ResultadoLogin CredenciaisInvalidas() =>
        new(StatusLogin.CredenciaisInvalidas, null, null);
}
