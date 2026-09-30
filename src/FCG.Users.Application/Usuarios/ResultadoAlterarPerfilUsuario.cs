namespace FCG.Users.Application.Usuarios;

public enum StatusAlteracaoPerfilUsuario
{
    Atualizado,
    DadosInvalidos,
    NaoEncontrado,
    PerfilNaoEncontrado
}

public sealed class ResultadoAlterarPerfilUsuario
{
    private ResultadoAlterarPerfilUsuario(
        StatusAlteracaoPerfilUsuario status,
        DadosUsuario? usuario,
        IReadOnlyDictionary<string, string[]>? erros)
    {
        Status = status;
        Usuario = usuario;
        Erros = erros ?? new Dictionary<string, string[]>();
    }

    public StatusAlteracaoPerfilUsuario Status { get; }
    public DadosUsuario? Usuario { get; }
    public IReadOnlyDictionary<string, string[]> Erros { get; }

    public static ResultadoAlterarPerfilUsuario Atualizado(DadosUsuario usuario) =>
        new(StatusAlteracaoPerfilUsuario.Atualizado, usuario, null);

    public static ResultadoAlterarPerfilUsuario DadosInvalidos(
        IReadOnlyDictionary<string, string[]> erros) =>
        new(StatusAlteracaoPerfilUsuario.DadosInvalidos, null, erros);

    public static ResultadoAlterarPerfilUsuario NaoEncontrado() =>
        new(StatusAlteracaoPerfilUsuario.NaoEncontrado, null, null);

    public static ResultadoAlterarPerfilUsuario PerfilNaoEncontrado() =>
        new(StatusAlteracaoPerfilUsuario.PerfilNaoEncontrado, null, null);
}
