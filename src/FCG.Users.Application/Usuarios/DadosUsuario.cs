using FCG.Users.Domain.Entities;

namespace FCG.Users.Application.Usuarios;

public sealed record DadosUsuario(
    Guid Id,
    string Nome,
    string Email,
    Guid PerfilId,
    bool Ativo,
    DateTimeOffset CriadoEmUtc,
    DateTimeOffset? DataInativacao)
{
    public static DadosUsuario De(Usuario usuario) => new(
        usuario.Id,
        usuario.Nome,
        usuario.Email,
        usuario.PerfilId,
        usuario.Ativo,
        usuario.CriadoEmUtc,
        usuario.DataInativacao);
}
