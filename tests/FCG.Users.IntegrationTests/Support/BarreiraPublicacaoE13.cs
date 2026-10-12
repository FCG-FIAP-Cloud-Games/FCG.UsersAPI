using Npgsql;

namespace FCG.Users.IntegrationTests.Support;

// Barreira instalada somente no PostgreSQL descartável. Nenhuma opção de falha é adicionada à API.
internal sealed class BarreiraPublicacaoE13 : IAsyncDisposable
{
    private const long Chave = 713130013;
    private readonly NpgsqlConnection _conexao;
    private bool _liberada;
    private bool _removida;
    private BarreiraPublicacaoE13(NpgsqlConnection conexao) => _conexao = conexao;

    internal static async Task<BarreiraPublicacaoE13> CriarAsync(string conexao, Guid eventId, CancellationToken cancelamento)
    {
        var banco = new NpgsqlConnection(conexao);
        await banco.OpenAsync(cancelamento);
        var instancia = new BarreiraPublicacaoE13(banco);
        try
        {
            await instancia.ExecutarAsync("SELECT pg_advisory_lock(713130013)", cancelamento);
            // O único valor interpolado é um UUID gerado pelo teste, formatado; nunca texto vindo do cliente.
            var sql = $"""
                CREATE FUNCTION e13_pausar_baixa() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM pg_advisory_xact_lock(713130013);
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER e13_pausar_baixa BEFORE UPDATE ON "tb_Outbox"
                FOR EACH ROW WHEN (NEW."EventId" = '{eventId:D}'::uuid
                    AND OLD."PublishedAt" IS NULL AND NEW."PublishedAt" IS NOT NULL)
                EXECUTE FUNCTION e13_pausar_baixa();
                """;
            await instancia.ExecutarAsync(sql, cancelamento);
            return instancia;
        }
        catch { await instancia.DisposeAsync(); throw; }
    }

    internal async Task<bool> HostEsperandoAsync(string nomeAplicacao, CancellationToken cancelamento)
    {
        await using var comando = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM pg_stat_activity
                WHERE application_name = @nome AND wait_event_type = 'Lock' AND wait_event = 'advisory')
            """, _conexao);
        comando.Parameters.AddWithValue("nome", nomeAplicacao);
        return (bool)(await comando.ExecuteScalarAsync(cancelamento))!;
    }

    internal async Task<bool> HostDesconectadoAsync(string nomeAplicacao, CancellationToken cancelamento)
    {
        await using var comando = new NpgsqlCommand("SELECT NOT EXISTS(SELECT 1 FROM pg_stat_activity WHERE application_name = @nome)", _conexao);
        comando.Parameters.AddWithValue("nome", nomeAplicacao);
        return (bool)(await comando.ExecuteScalarAsync(cancelamento))!;
    }

    internal async Task LiberarAsync(CancellationToken cancelamento)
    {
        if (_liberada) return;
        await ExecutarAsync($"SELECT pg_advisory_unlock({Chave})", cancelamento);
        _liberada = true;
    }

    internal async Task RemoverAsync(CancellationToken cancelamento)
    {
        await LiberarAsync(cancelamento);
        if (_removida) return;
        await ExecutarAsync("DROP TRIGGER IF EXISTS e13_pausar_baixa ON \"tb_Outbox\"; DROP FUNCTION IF EXISTS e13_pausar_baixa()", cancelamento);
        _removida = true;
    }

    private async Task ExecutarAsync(string sql, CancellationToken cancelamento)
    {
        await using var comando = new NpgsqlCommand(sql, _conexao) { CommandTimeout = 10 };
        await comando.ExecuteNonQueryAsync(cancelamento);
    }

    public async ValueTask DisposeAsync()
    {
        try { await RemoverAsync(CancellationToken.None); }
        finally { await _conexao.DisposeAsync(); }
    }
}
