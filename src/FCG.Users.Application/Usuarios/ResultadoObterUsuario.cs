namespace FCG.Users.Application.Usuarios;

public enum StatusObtencaoUsuario
{
    Encontrado,
    IdInvalido,
    NaoEncontrado
}

public sealed record ResultadoObterUsuario(StatusObtencaoUsuario Status, DadosUsuario? Usuario)
{
    public static ResultadoObterUsuario Encontrado(DadosUsuario usuario) =>
        new(StatusObtencaoUsuario.Encontrado, usuario);

    public static ResultadoObterUsuario IdInvalido() =>
        new(StatusObtencaoUsuario.IdInvalido, null);

    public static ResultadoObterUsuario NaoEncontrado() =>
        new(StatusObtencaoUsuario.NaoEncontrado, null);
}
