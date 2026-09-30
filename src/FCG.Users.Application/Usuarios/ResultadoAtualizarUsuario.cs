namespace FCG.Users.Application.Usuarios;

public enum StatusAtualizacaoUsuario
{
    Atualizado,
    DadosInvalidos,
    NaoEncontrado,
    EmailJaCadastrado
}

public sealed class ResultadoAtualizarUsuario
{
    private ResultadoAtualizarUsuario(
        StatusAtualizacaoUsuario status,
        DadosUsuario? usuario,
        IReadOnlyDictionary<string, string[]>? erros)
    {
        Status = status;
        Usuario = usuario;
        Erros = erros ?? new Dictionary<string, string[]>();
    }

    public StatusAtualizacaoUsuario Status { get; }
    public DadosUsuario? Usuario { get; }
    public IReadOnlyDictionary<string, string[]> Erros { get; }

    public static ResultadoAtualizarUsuario Atualizado(DadosUsuario usuario) =>
        new(StatusAtualizacaoUsuario.Atualizado, usuario, null);

    public static ResultadoAtualizarUsuario DadosInvalidos(IReadOnlyDictionary<string, string[]> erros) =>
        new(StatusAtualizacaoUsuario.DadosInvalidos, null, erros);

    public static ResultadoAtualizarUsuario NaoEncontrado() =>
        new(StatusAtualizacaoUsuario.NaoEncontrado, null, null);

    public static ResultadoAtualizarUsuario ConflitoEmail() =>
        new(StatusAtualizacaoUsuario.EmailJaCadastrado, null, null);
}
