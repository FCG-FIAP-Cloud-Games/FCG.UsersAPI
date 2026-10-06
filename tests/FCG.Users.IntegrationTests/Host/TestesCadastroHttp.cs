using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FCG.Users.Application.Abstractions.Security;
using FCG.Users.Domain.Entities;
using FCG.Users.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FCG.Users.IntegrationTests.Host;

[Collection(ColecaoBancoUsers.Nome)]
public sealed class TestesCadastroHttp(BancoUsersFixture banco)
{
    private const string Rota = "/api/v1/usuarios";
    private const string SenhaTeste = "SenhaCadastro@42";
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CadastroNormalizaDadosGravaHashEAuditoriaESempreUsaPerfilComum()
    {
        using var logs = new ColetorLogs();
        await using var fabrica = CriarFabrica(logs);
        using var cliente = fabrica.CreateClient();
        var cpf = DadosPersistencia.ProximoCpf();
        var email = $"e06-{Guid.NewGuid():N}@exemplo.test";
        var cpfFormatado = $"{cpf[..3]}.{cpf[3..6]}.{cpf[6..9]}-{cpf[9..]}";
        var antes = DateTimeOffset.UtcNow;
        using var resposta = await cliente.PostAsJsonAsync(Rota, new
        {
            nome = "  Maria   da Silva  ", cpf = cpfFormatado, dataNascimento = "2000-02-29",
            email = $"  {email.ToUpperInvariant()}  ", senha = SenhaTeste,
            perfilId = PerfisSistema.AdministradorId, ativo = false,
            senhaHash = "hash-injetado-pelo-cliente", id = Guid.Empty, criadoEmUtc = "1900-01-01T00:00:00Z"
        }, Cancelamento);
        var depois = DateTimeOffset.UtcNow;
        using var json = await LerJsonAsync(resposta);
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var id = json.RootElement.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal($"{Rota}/{id}", resposta.Headers.Location?.OriginalString);
        Assert.Equal("Maria da Silva", json.RootElement.GetProperty("nome").GetString());
        Assert.Equal(email, json.RootElement.GetProperty("email").GetString());
        Assert.Equal(PerfisSistema.UsuarioId, json.RootElement.GetProperty("perfilId").GetGuid());
        Assert.True(json.RootElement.GetProperty("ativo").GetBoolean());
        foreach (var segredo in new[] { "senha", "senhaHash", "cpf", "token", "refreshToken" })
            Assert.False(json.RootElement.TryGetProperty(segredo, out _));
        await using var contexto = banco.CriarContexto();
        var usuario = await contexto.Usuarios.SingleAsync(item => item.Id == id, Cancelamento);
        Assert.Equal(cpf, usuario.CPF);
        Assert.Equal(email, usuario.Email);
        Assert.Equal(new DateOnly(2000, 2, 29), usuario.DataNascimento);
        Assert.Equal(PerfisSistema.UsuarioId, usuario.PerfilId);
        Assert.InRange(usuario.CriadoEmUtc, antes.AddMilliseconds(-1), depois);
        Assert.NotEqual(SenhaTeste, usuario.SenhaHash);
        using var escopo = fabrica.Services.CreateScope();
        Assert.True(escopo.ServiceProvider.GetRequiredService<IServicoHashSenha>().Verificar(SenhaTeste, usuario.SenhaHash));
        var log = Assert.Single(await contexto.LogsUsuarios.Where(item => item.UsuarioId == id).ToListAsync(Cancelamento));
        Assert.Equal("Usuário cadastrado.", log.Descricao);
        Assert.Equal(usuario.CriadoEmUtc, log.DataCriacao);
        Assert.DoesNotContain(SenhaTeste, logs.Texto, StringComparison.Ordinal);
        Assert.DoesNotContain(usuario.SenhaHash, logs.Texto, StringComparison.Ordinal);
        Assert.DoesNotContain(cpf, logs.Texto, StringComparison.Ordinal);
        Assert.DoesNotContain(email, logs.Texto, StringComparison.Ordinal);
        Assert.DoesNotContain("hash-injetado-pelo-cliente", json.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nome", "Al")]
    [InlineData("cpf", "1234567890")]
    [InlineData("cpf", "１２３４５６７８９０１")]
    [InlineData("email", "email-invalido")]
    [InlineData("senha", "fraca")]
    [InlineData("dataNascimento", "9999-12-31")]
    [InlineData("dataNascimento", "0001-01-01")]
    public async Task DadosInvalidosRetornam400SemGravarUsuarioOuLog(string campo, string valor)
    {
        await using var fabrica = CriarFabrica();
        using var cliente = fabrica.CreateClient();
        var dados = NovoCadastro();
        dados[campo] = valor;
        await using var verificacao = banco.CriarContexto();
        var usuariosAntes = await verificacao.Usuarios.CountAsync(Cancelamento);
        var logsAntes = await verificacao.LogsUsuarios.CountAsync(Cancelamento);
        using var resposta = await cliente.PostAsJsonAsync(Rota, dados, Cancelamento);
        using var json = await LerJsonAsync(resposta);
        ValidarProblema(resposta, json, HttpStatusCode.BadRequest);
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty(campo, out _));
        Assert.Equal(usuariosAntes, await verificacao.Usuarios.CountAsync(Cancelamento));
        Assert.Equal(logsAntes, await verificacao.LogsUsuarios.CountAsync(Cancelamento));
        Assert.DoesNotContain(SenhaTeste, json.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"dataNascimento\":\"2000-02-30\"}")]
    [InlineData("{\"dataNascimento\":\"2000-02-29T00:00:00Z\"}")]
    [InlineData("null")]
    public async Task CorpoInvalidoOuDataComHorarioRetorna400(string corpo)
    {
        await using var fabrica = CriarFabrica();
        using var cliente = fabrica.CreateClient();
        using var conteudo = new StringContent(corpo, Encoding.UTF8, "application/json");
        using var resposta = await cliente.PostAsync(Rota, conteudo, Cancelamento);
        using var json = await LerJsonAsync(resposta);
        ValidarProblema(resposta, json, HttpStatusCode.BadRequest);
        Assert.NotEmpty(json.RootElement.GetProperty("errors").EnumerateObject());
    }

    [Theory]
    [InlineData("email")]
    [InlineData("cpf")]
    public async Task DuplicidadeRetorna409EPreservaSomenteAuditoriaDoCadastroAceito(string campo)
    {
        await using var fabrica = CriarFabrica();
        using var cliente = fabrica.CreateClient();
        var primeiro = NovoCadastro();
        using var criado = await cliente.PostAsJsonAsync(Rota, primeiro, Cancelamento);
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
        using var dadosCriado = await LerJsonAsync(criado);
        var id = dadosCriado.RootElement.GetProperty("id").GetGuid();
        var segundo = NovoCadastro();
        segundo[campo] = campo == "email" ? $" {primeiro[campo].ToString()!.ToUpperInvariant()} " : primeiro[campo];
        await using var verificacao = banco.CriarContexto();
        var usuariosAntes = await verificacao.Usuarios.CountAsync(Cancelamento);
        var logsAntes = await verificacao.LogsUsuarios.CountAsync(Cancelamento);
        using var resposta = await cliente.PostAsJsonAsync(Rota, segundo, Cancelamento);
        using var json = await LerJsonAsync(resposta);
        ValidarProblema(resposta, json, HttpStatusCode.Conflict);
        Assert.Equal(usuariosAntes, await verificacao.Usuarios.CountAsync(Cancelamento));
        Assert.Equal(logsAntes, await verificacao.LogsUsuarios.CountAsync(Cancelamento));
        Assert.Equal(1, await verificacao.LogsUsuarios.CountAsync(log => log.UsuarioId == id, Cancelamento));
        Assert.DoesNotContain(SenhaTeste, json.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("email")]
    [InlineData("cpf")]
    public async Task RequisicoesConcorrentesRetornamUm201Um409EUmUnicoLog(string campo)
    {
        await using var fabrica = CriarFabrica();
        using var cliente = fabrica.CreateClient();
        var primeiro = NovoCadastro();
        var segundo = NovoCadastro();
        segundo[campo] = primeiro[campo];
        var respostas = await Task.WhenAll(cliente.PostAsJsonAsync(Rota, primeiro, Cancelamento),
            cliente.PostAsJsonAsync(Rota, segundo, Cancelamento));
        try
        {
            var criado = Assert.Single(respostas, resposta => resposta.StatusCode == HttpStatusCode.Created);
            var conflito = Assert.Single(respostas, resposta => resposta.StatusCode == HttpStatusCode.Conflict);
            using var json = await LerJsonAsync(criado);
            using var erro = await LerJsonAsync(conflito);
            ValidarProblema(conflito, erro, HttpStatusCode.Conflict);
            var id = json.RootElement.GetProperty("id").GetGuid();
            var emailA = primeiro["email"].ToString();
            var emailB = segundo["email"].ToString();
            await using var contexto = banco.CriarContexto();
            Assert.Equal(1, await contexto.Usuarios.CountAsync(item => item.Email == emailA || item.Email == emailB, Cancelamento));
            Assert.Equal(1, await contexto.LogsUsuarios.CountAsync(log => log.UsuarioId == id, Cancelamento));
        }
        finally
        {
            foreach (var resposta in respostas) resposta.Dispose();
        }
    }

    [Fact]
    public async Task FalhaDeBancoRetorna500ComTraceIdSemExporDadosInternos()
    {
        var conexao = new NpgsqlConnectionStringBuilder(banco.Conexao) { Database = "banco_inexistente_e06" };
        await using var fabrica = new FabricaUsersApi("Production", conexao: conexao.ConnectionString);
        using var cliente = fabrica.CreateClient();
        using var resposta = await cliente.PostAsJsonAsync(Rota, NovoCadastro(), Cancelamento);
        using var json = await LerJsonAsync(resposta);
        ValidarProblema(resposta, json, HttpStatusCode.InternalServerError);
        Assert.Equal("Erro interno no servidor.", json.RootElement.GetProperty("title").GetString());
        foreach (var segredo in new[] { SenhaTeste, conexao.Password!, "banco_inexistente_e06", "Npgsql", "SELECT", "SenhaHash" })
            Assert.DoesNotContain(segredo, json.RootElement.GetRawText(), StringComparison.Ordinal);
    }


    [Fact]
    public async Task FalhaNaAuditoriaRetorna500EDesfazCadastroSemExporSegredosNosLogs()
    {
        using var logs = new ColetorLogs();
        await using var fabrica = CriarFabrica(logs);
        using var cliente = fabrica.CreateClient();
        var dados = NovoCadastro();
        var email = dados["email"].ToString()!;
        var cpf = dados["cpf"].ToString()!;
        await using var bancoTeste = banco.CriarContexto();
        const string criarFuncao = """
            CREATE FUNCTION e06_rejeitar_auditoria() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF EXISTS (SELECT 1 FROM "tb_Usuarios" WHERE "Id" = NEW."UsuarioId" AND "Email" = TG_ARGV[0]) THEN
                    RAISE EXCEPTION 'Falha simulada na auditoria E06.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_E06_AuditoriaHttp';
                END IF;
                RETURN NEW;
            END;
            $$;
            """;
        await bancoTeste.Database.ExecuteSqlRawAsync(criarFuncao, Cancelamento);
        try
        {
            // E-mail sintético construído apenas com prefixo, Guid e domínio fixos, nunca entrada externa.
            var criarGatilho = "CREATE TRIGGER e06_falha_auditoria BEFORE INSERT ON \"tb_LogUsuarios\" "
                + "FOR EACH ROW EXECUTE FUNCTION e06_rejeitar_auditoria('" + email + "')";
            await bancoTeste.Database.ExecuteSqlRawAsync(criarGatilho, Cancelamento);
            var logsAntes = await bancoTeste.LogsUsuarios.CountAsync(Cancelamento);
            using var resposta = await cliente.PostAsJsonAsync(Rota, dados, Cancelamento);
            using var json = await LerJsonAsync(resposta);
            ValidarProblema(resposta, json, HttpStatusCode.InternalServerError);
            Assert.False(await bancoTeste.Usuarios.AnyAsync(item => item.Email == email || item.CPF == cpf, Cancelamento));
            Assert.Equal(logsAntes, await bancoTeste.LogsUsuarios.CountAsync(Cancelamento));
            var texto = json.RootElement.GetRawText();
            foreach (var segredo in new[] { SenhaTeste, email, cpf, "PBKDF2-SHA256$" })
            {
                Assert.DoesNotContain(segredo, texto, StringComparison.Ordinal);
                Assert.DoesNotContain(segredo, logs.Texto, StringComparison.Ordinal);
            }
            Assert.DoesNotContain("CK_E06_AuditoriaHttp", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("Npgsql", texto, StringComparison.Ordinal);
            // O diagnóstico técnico permanece disponível, sem o corpo da requisição ou valores SQL.
            Assert.Contains("CK_E06_AuditoriaHttp", logs.Texto, StringComparison.Ordinal);
        }
        finally
        {
            await bancoTeste.Database.ExecuteSqlRawAsync(
                "DROP TRIGGER IF EXISTS e06_falha_auditoria ON \"tb_LogUsuarios\"; DROP FUNCTION IF EXISTS e06_rejeitar_auditoria()",
                CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("GET", "/api/v1/usuarios")]
    [InlineData("PUT", "/api/v1/usuarios")]
    [InlineData("DELETE", "/api/v1/usuarios")]
    [InlineData("GET", "/api/v1/usuarios/11111111-1111-1111-1111-111111111111")]
    public async Task EntregaNaoExpoeConsultasOuEdicoesAntesDaE09(string metodo, string rota)
    {
        await using var fabrica = CriarFabrica();
        using var cliente = fabrica.CreateClient();
        using var requisicao = new HttpRequestMessage(new HttpMethod(metodo), rota);
        using var resposta = await cliente.SendAsync(requisicao, Cancelamento);
        Assert.True(resposta.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
    }

    private FabricaUsersApi CriarFabrica(ColetorLogs? logs = null) => new(conexao: banco.Conexao,
        configurarServicos: logs is null ? null : servicos => servicos.AddLogging(logging => logging.AddProvider(logs)));

    private static Dictionary<string, object> NovoCadastro() => new(StringComparer.Ordinal)
    {
        ["nome"] = "Pessoa de teste", ["cpf"] = DadosPersistencia.ProximoCpf(), ["dataNascimento"] = "2000-02-29",
        ["email"] = $"e06-{Guid.NewGuid():N}@exemplo.test", ["senha"] = SenhaTeste
    };

    private static async Task<JsonDocument> LerJsonAsync(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Cancelamento));

    private static void ValidarProblema(HttpResponseMessage resposta, JsonDocument json, HttpStatusCode esperado)
    {
        Assert.Equal(esperado, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.Equal((int)esperado, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(Rota, json.RootElement.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
    }
}
