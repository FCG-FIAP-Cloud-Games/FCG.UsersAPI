using Npgsql;

namespace FCG.Users.Infrastructure.Data;

internal static class ConfiguracaoBanco
{
    internal static string ValidarConexao(string? conexao)
    {
        if (string.IsNullOrWhiteSpace(conexao))
            throw new InvalidOperationException(
                "Configure ConnectionStrings:UsersDatabase antes de iniciar a persistência do UsersAPI.");

        NpgsqlConnectionStringBuilder configuracao;
        try
        {
            configuracao = new NpgsqlConnectionStringBuilder(conexao);
        }
        catch (ArgumentException)
        {
            // Não incluir a conexão ou a exceção original: elas podem conter credenciais.
            throw new InvalidOperationException("ConnectionStrings:UsersDatabase tem formato inválido.");
        }

        if (string.IsNullOrWhiteSpace(configuracao.Host)
            || string.IsNullOrWhiteSpace(configuracao.Database)
            || string.IsNullOrWhiteSpace(configuracao.Username))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:UsersDatabase deve informar Host, Database e Username.");
        }

        return conexao;
    }
}
