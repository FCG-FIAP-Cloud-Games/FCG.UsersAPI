namespace FCG.Users.Application.Auth;

public enum StatusRenovacaoToken
{
    Sucesso,
    DadosInvalidos,
    TokenInvalido
}

public sealed class ResultadoRenovarToken
{
    private ResultadoRenovarToken(
        StatusRenovacaoToken status,
        LoginRealizado? login,
        IReadOnlyDictionary<string, string[]>? erros)
    {
        Status = status;
        Login = login;
        Erros = erros ?? new Dictionary<string, string[]>();
    }

    public StatusRenovacaoToken Status { get; }
    public LoginRealizado? Login { get; }
    public IReadOnlyDictionary<string, string[]> Erros { get; }

    public static ResultadoRenovarToken Renovado(LoginRealizado login) =>
        new(StatusRenovacaoToken.Sucesso, login, null);

    public static ResultadoRenovarToken DadosInvalidos(
        IReadOnlyDictionary<string, string[]> erros) =>
        new(StatusRenovacaoToken.DadosInvalidos, null, erros);

    public static ResultadoRenovarToken TokenInvalido() =>
        new(StatusRenovacaoToken.TokenInvalido, null, null);
}
