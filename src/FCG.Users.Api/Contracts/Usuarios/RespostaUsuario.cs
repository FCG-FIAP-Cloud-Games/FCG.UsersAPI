namespace FCG.Users.Api.Contracts.Usuarios;

public sealed record RespostaUsuario(
    Guid Id,
    string Nome,
    string Email,
    Guid PerfilId,
    bool Ativo,
    DateTimeOffset CriadoEmUtc,
    DateTimeOffset? DataInativacao);
