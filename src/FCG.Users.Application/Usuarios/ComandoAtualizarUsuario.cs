namespace FCG.Users.Application.Usuarios;

public sealed record ComandoAtualizarUsuario(
    Guid Id,
    string Nome,
    DateTimeOffset DataNascimento,
    string Email);
