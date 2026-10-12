using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FCG.Users.Application.Messaging;
using FCG.Users.Domain.Entities;
using FCG.Users.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FCG.Users.IntegrationTests.Host;

[Collection(ColecaoBancoUsers.Nome)]
public sealed class TestesOutboxCadastroHttp(BancoUsersFixture banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;
    private const string Senha = "SenhaSintetica#E11";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CadastroPublicoEAdministrativoGravamEventoComCorrelacaoSemDependerDoBroker(bool administrativo, bool informarCorrelacao)
    {
        await using var fabrica = new FabricaUsersApi(conexao: banco.Conexao, sobrescreverConfiguracao:
            new Dictionary<string, string?> { ["RabbitMq:Host"] = "127.0.0.1", ["RabbitMq:Port"] = "1" });
        using var cliente = fabrica.CreateClient();
        var dados = Cadastro();
        var correlacao = Guid.NewGuid();
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, administrativo ? "/api/v1/usuarios/administradores" : "/api/v1/usuarios")
        { Content = JsonContent.Create(dados) };
        if (administrativo) requisicao.Headers.Authorization = new("Bearer", DadosE09.Jwt(fabrica, Guid.NewGuid(), administrador: true));
        if (informarCorrelacao) requisicao.Headers.Add("X-Correlation-ID", correlacao.ToString("D"));
        using var resposta = await cliente.SendAsync(requisicao, Cancelamento);
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var devolvida = Guid.Parse(Assert.Single(resposta.Headers.GetValues("X-Correlation-ID")));
        Assert.NotEqual(Guid.Empty, devolvida);
        if (informarCorrelacao) Assert.Equal(correlacao, devolvida);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Cancelamento));
        var id = json.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(administrativo ? PerfisSistema.AdministradorId : PerfisSistema.UsuarioId, json.RootElement.GetProperty("perfilId").GetGuid());
        Assert.Equal(7, json.RootElement.EnumerateObject().Count());
        await using var leitura = banco.CriarContexto();
        var usuario = await leitura.Usuarios.SingleAsync(item => item.Id == id, Cancelamento);
        var pendente = await leitura.MensagensOutbox.SingleAsync(item => item.UserId == id, Cancelamento);
        Assert.Equal(1, await leitura.LogsUsuarios.CountAsync(item => item.UsuarioId == id, Cancelamento));
        Assert.Equal(devolvida, pendente.CorrelationId);
        Assert.Equal(DefinicaoUserCreatedEvent.Tipo, pendente.EventType);
        Assert.Null(pendente.PublishedAt);
        Assert.Equal(0, pendente.Attempts);
        using var payload = JsonDocument.Parse(pendente.Payload);
        Assert.Equal(usuario.Nome, payload.RootElement.GetProperty("name").GetString());
        Assert.Equal(usuario.Email, payload.RootElement.GetProperty("email").GetString());
        Assert.Equal(usuario.CriadoEmUtc, payload.RootElement.GetProperty("occurredAt").GetDateTimeOffset());
        Assert.Equal(pendente.EventId, payload.RootElement.GetProperty("eventId").GetGuid());
        Assert.Equal(devolvida, payload.RootElement.GetProperty("correlationId").GetGuid());
        Assert.DoesNotContain(Senha, pendente.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain(usuario.SenhaHash, pendente.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain(usuario.CPF, pendente.Payload, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("texto-invalido")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("ee980790e7e448bca46a0ea31b92d463")]
    [InlineData("11111111-1111-1111-1111-111111111111,22222222-2222-2222-2222-222222222222")]
    public async Task CorrelacaoHttpInvalidaRetorna400AntesDeAcessarBanco(string valor)
    {
        await using var fabrica = new FabricaUsersApi(); // Banco inacessível: validação deve vir primeiro.
        using var cliente = fabrica.CreateClient();
        if (valor.Length == 0)
        {
            // HttpClient remove o cabeçalho vazio. Injeta a presença explícita no pipeline do TestServer.
            var contexto = await fabrica.Server.SendAsync(contexto =>
            {
                contexto.Request.Method = "POST";
                contexto.Request.Path = "/api/v1/usuarios";
                contexto.Request.ContentType = "application/json";
                contexto.Request.Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(Cadastro()));
                contexto.Request.Headers["X-Correlation-ID"] = new Microsoft.Extensions.Primitives.StringValues("");
            }, Cancelamento);
            Assert.Equal(400, contexto.Response.StatusCode);
            using var leitor = new StreamReader(contexto.Response.Body);
            using var problema = JsonDocument.Parse(await leitor.ReadToEndAsync(Cancelamento));
            Assert.True(problema.RootElement.GetProperty("errors").TryGetProperty("correlationId", out _));
            return;
        }
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuarios") { Content = JsonContent.Create(Cadastro()) };
        Assert.True(requisicao.Headers.TryAddWithoutValidation("X-Correlation-ID", valor));
        using var resposta = await cliente.SendAsync(requisicao, Cancelamento);
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Cancelamento));
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("correlationId", out _));
    }

    [Theory]
    [InlineData("nome")]
    [InlineData("email")]
    [InlineData("cpf")]
    public async Task CadastroInvalidoNaoAcrescentaUsuarioLogOuOutbox(string campo)
    {
        await using var fabrica = new FabricaUsersApi(conexao: banco.Conexao);
        using var cliente = fabrica.CreateClient();
        var dados = Cadastro();
        dados[campo] = "invalido";
        if (campo == "nome") dados[campo] = "";
        await using var leitura = banco.CriarContexto();
        var antes = await ContagensAsync(leitura);
        using var resposta = await cliente.PostAsJsonAsync("/api/v1/usuarios", dados, Cancelamento);
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal(antes, await ContagensAsync(leitura));
    }

    [Theory]
    [InlineData("email")]
    [InlineData("cpf")]
    public async Task DuplicidadeRetorna409SemCriarOutroEvento(string campo)
    {
        await using var fabrica = new FabricaUsersApi(conexao: banco.Conexao);
        using var cliente = fabrica.CreateClient();
        var original = Cadastro();
        using var criado = await cliente.PostAsJsonAsync("/api/v1/usuarios", original, Cancelamento);
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
        var repetido = Cadastro();
        repetido[campo] = original[campo];
        await using var leitura = banco.CriarContexto();
        var antes = await ContagensAsync(leitura);
        using var resposta = await cliente.PostAsJsonAsync("/api/v1/usuarios", repetido, Cancelamento);
        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal(antes, await ContagensAsync(leitura));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FalhaNaOutboxRetorna500SemCadastroParcialOuPayloadNosLogs(bool noCommit)
    {
        using var logs = new ColetorLogs();
        await using var fabrica = new FabricaUsersApi("Production", conexao: banco.Conexao,
            configurarServicos: services => services.AddLogging(logging => logging.AddProvider(logs)));
        using var cliente = fabrica.CreateClient();
        var dados = Cadastro();
        var correlacao = Guid.NewGuid();
        await using var contexto = banco.CriarContexto();
        await contexto.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION e11_rejeitar_outbox_http() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW."CorrelationId" = TG_ARGV[0]::uuid THEN
                    RAISE EXCEPTION 'Falha sintética na Outbox E11.' USING ERRCODE = '23514', CONSTRAINT = 'CK_E11_OutboxHttp';
                END IF;
                RETURN NEW;
            END;
            $$;
            """, Cancelamento);
        try
        {
            var gatilho = noCommit
                ? "CREATE CONSTRAINT TRIGGER e11_outbox_http AFTER INSERT ON \"tb_Outbox\" DEFERRABLE INITIALLY DEFERRED"
                : "CREATE TRIGGER e11_outbox_http BEFORE INSERT ON \"tb_Outbox\"";
            await contexto.Database.ExecuteSqlRawAsync(gatilho + $" FOR EACH ROW EXECUTE FUNCTION e11_rejeitar_outbox_http('{correlacao:D}')", Cancelamento);
            var antes = await ContagensAsync(contexto);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuarios") { Content = JsonContent.Create(dados) };
            request.Headers.Add("X-Correlation-ID", correlacao.ToString("D"));
            using var resposta = await cliente.SendAsync(request, Cancelamento);
            Assert.Equal(HttpStatusCode.InternalServerError, resposta.StatusCode);
            Assert.Equal(antes, await ContagensAsync(contexto));
            var texto = await resposta.Content.ReadAsStringAsync(Cancelamento);
            foreach (var segredo in new[] { Senha, dados["nome"].ToString()!, dados["email"].ToString()!, dados["cpf"].ToString()! })
            {
                Assert.DoesNotContain(segredo, texto, StringComparison.Ordinal);
                Assert.DoesNotContain(segredo, logs.Texto, StringComparison.Ordinal);
            }
            Assert.DoesNotContain("CK_E11_OutboxHttp", texto, StringComparison.Ordinal);
            Assert.Contains("CK_E11_OutboxHttp", logs.Texto, StringComparison.Ordinal);
        }
        finally { await contexto.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS e11_outbox_http ON \"tb_Outbox\"; DROP FUNCTION IF EXISTS e11_rejeitar_outbox_http()", CancellationToken.None); }
    }

    private static Dictionary<string, object> Cadastro() => new(StringComparer.Ordinal)
    {
        ["nome"] = "  Pessoa   Sintética E11 ", ["cpf"] = DadosPersistencia.ProximoCpf(),
        ["email"] = $"e11-{Guid.NewGuid():N}@exemplo.test", ["dataNascimento"] = "2000-01-01", ["senha"] = Senha
    };

    private static async Task<(int Usuarios, int Logs, int Eventos)> ContagensAsync(FCG.Users.Infrastructure.Data.UsersDbContext contexto) =>
        (await contexto.Usuarios.CountAsync(Cancelamento), await contexto.LogsUsuarios.CountAsync(Cancelamento), await contexto.MensagensOutbox.CountAsync(Cancelamento));
}
