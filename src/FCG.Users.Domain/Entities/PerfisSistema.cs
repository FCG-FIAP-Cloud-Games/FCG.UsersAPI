namespace FCG.Users.Domain.Entities;

/// <summary>
/// Identificadores estáveis dos perfis usados pelas regras de autenticação e autorização.
/// </summary>
public static class PerfisSistema
{
    public static readonly Guid UsuarioId = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid AdministradorId = new("22222222-2222-2222-2222-222222222222");

    public const string Usuario = "Usuario";
    public const string Administrador = "Administrador";

    public static string ObterNome(Guid perfilId) => perfilId switch
    {
        var id when id == UsuarioId => Usuario,
        var id when id == AdministradorId => Administrador,
        _ => throw new ArgumentOutOfRangeException(nameof(perfilId), "Perfil desconhecido.")
    };
}
