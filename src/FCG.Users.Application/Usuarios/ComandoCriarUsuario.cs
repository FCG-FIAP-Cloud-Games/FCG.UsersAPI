namespace FCG.Users.Application.Usuarios;

public sealed record ComandoCriarUsuario(
    string Nome,
    string CPF,
    DateOnly DataNascimento,
    string Email,
    string Senha,
    Guid PerfilId);
