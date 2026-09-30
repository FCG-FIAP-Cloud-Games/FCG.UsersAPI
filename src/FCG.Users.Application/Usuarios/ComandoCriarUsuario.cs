namespace FCG.Users.Application.Usuarios;

public sealed record ComandoCriarUsuario(
    string Nome,
    string CPF,
    DateTimeOffset DataNascimento,
    string Email,
    string Senha,
    Guid PerfilId);
