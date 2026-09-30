namespace FCG.Users.Application.Abstractions.Security;

public interface IServicoRefreshToken
{
    RefreshTokenGerado GerarToken();

    string CalcularHash(string token);
}

public sealed record RefreshTokenGerado(
    string Valor,
    string Hash,
    DateTimeOffset CriadoEm,
    DateTimeOffset ExpiraEm);
