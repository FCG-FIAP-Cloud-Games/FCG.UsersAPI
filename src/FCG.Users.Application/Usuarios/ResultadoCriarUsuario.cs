namespace FCG.Users.Application.Usuarios;

public enum StatusCriacaoUsuario
{
    Criado,
    DadosInvalidos,
    EmailJaCadastrado,
    CpfJaCadastrado,
    PerfilNaoEncontrado
}

public sealed class ResultadoCriarUsuario
{
    private ResultadoCriarUsuario(
        StatusCriacaoUsuario status,
        DadosUsuario? usuario,
        IReadOnlyDictionary<string, string[]>? erros)
    {
        Status = status;
        Usuario = usuario;
        Erros = erros ?? new Dictionary<string, string[]>();
    }

    public StatusCriacaoUsuario Status { get; }
    public DadosUsuario? Usuario { get; }
    public IReadOnlyDictionary<string, string[]> Erros { get; }

    public static ResultadoCriarUsuario Criado(DadosUsuario usuario) =>
        new(StatusCriacaoUsuario.Criado, usuario, null);

    public static ResultadoCriarUsuario DadosInvalidos(IReadOnlyDictionary<string, string[]> erros) =>
        new(StatusCriacaoUsuario.DadosInvalidos, null, erros);

    public static ResultadoCriarUsuario ConflitoEmail() =>
        new(StatusCriacaoUsuario.EmailJaCadastrado, null, null);

    public static ResultadoCriarUsuario ConflitoCpf() =>
        new(StatusCriacaoUsuario.CpfJaCadastrado, null, null);

    public static ResultadoCriarUsuario PerfilNaoEncontrado() =>
        new(StatusCriacaoUsuario.PerfilNaoEncontrado, null, null);
}
