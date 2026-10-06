using System.Data.Common;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FCG.Users.Api.Contracts.Auth;
using FCG.Users.Application.Abstractions.Security;
using FCG.Users.Domain.Entities;
using FCG.Users.Infrastructure.Data;
using FCG.Users.Infrastructure.Security;
using FCG.Users.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FCG.Users.IntegrationTests.Host;

[Collection(ColecaoBancoUsers.Nome)]
public sealed class TestesSessaoHttp(BancoUsersFixture banco)
{
    private const string Refresh = "/api/v1/auth/refresh";
    private const string Logout = "/api/v1/auth/logout";
    private const string Senha = "SenhaSessao@E08";
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LoginRefreshReusoENovaRotacaoMantemUmaUnicaSessaoRenovavelSemExporSegredos()
    {
        var usuario = await GravarUsuarioAsync();
        using var logs = new ColetorLogs();
        await using var fabrica = CriarFabrica(logs);
        using var cliente = fabrica.CreateClient();
        var login = await LoginAsync(cliente, usuario);
        // Refresh funciona mesmo sem access token, inclusive quando Authorization traz um JWT vencido.
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            JwtTeste.Criar(fabrica.Chaves, corpo => corpo["exp"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds()));
        using var resposta = await RenovarAsync(cliente, login.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        ValidarSemCache(resposta);
        var renovado = (await resposta.Content.ReadFromJsonAsync<RespostaLogin>(Cancelamento))!;
        Assert.NotEqual(login.AccessToken, renovado.AccessToken);
        Assert.NotEqual(login.RefreshToken, renovado.RefreshToken);
        Assert.Equal("Bearer", renovado.TokenType);
        Assert.Equal(900, renovado.ExpiresIn);
        Assert.Equal(usuario.Id, renovado.Usuario.Id);
        Assert.Equal(usuario.Nome, renovado.Usuario.Nome);
        Assert.Equal(usuario.Email, renovado.Usuario.Email);
        Assert.Equal(PerfisSistema.Usuario, renovado.Usuario.Perfil);
        ValidarJwt(renovado, fabrica.Chaves);
        using var repetido = await RenovarAsync(cliente, login.RefreshToken);
        await ValidarProblemaAsync(repetido, HttpStatusCode.Unauthorized, Refresh);
        using var segunda = await RenovarAsync(cliente, renovado.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        var ultimo = (await segunda.Content.ReadFromJsonAsync<RespostaLogin>(Cancelamento))!;
        await using var contexto = banco.CriarContexto();
        var tokens = await contexto.Tokens.AsNoTracking().Where(item => item.UsuarioId == usuario.Id).ToListAsync(Cancelamento);
        Assert.Equal(3, tokens.Count);
        Assert.Equal(2, tokens.Count(item => item.EstaRevogado()));
        var ativo = Assert.Single(tokens, item => !item.EstaRevogado());
        Assert.Equal(Hash(ultimo.RefreshToken), ativo.TokenHash);
        Assert.Equal(TimeSpan.FromDays(7), ativo.DataExpiracao - ativo.DataCriacao);
        Assert.All(tokens, item => Assert.Equal(64, item.TokenHash.Length));
        Assert.Empty(await contexto.LogsUsuarios.Where(item => item.UsuarioId == usuario.Id).ToListAsync(Cancelamento));
        foreach (var segredo in new[] { Senha, usuario.SenhaHash, usuario.CPF, usuario.Email,
            login.AccessToken, login.RefreshToken, renovado.AccessToken, renovado.RefreshToken,
            ultimo.AccessToken, ultimo.RefreshToken, ativo.TokenHash })
            Assert.DoesNotContain(segredo, logs.Texto, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"refreshToken\":null}")]
    [InlineData("{\"refreshToken\":\"\"}")]
    [InlineData("{\"refreshToken\":\"   \"}")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"refreshToken\":123}")]
    public async Task RefreshComEntradaInvalidaRetorna400SemPrecisarDeBanco(string corpo)
    {
        await using var fabrica = new FabricaUsersApi(); // conexão indisponível: validação vem antes do banco
        using var cliente = fabrica.CreateClient();
        using var conteudo = new StringContent(corpo, Encoding.UTF8, "application/json");
        using var resposta = await cliente.PostAsync(Refresh, conteudo, Cancelamento);
        await ValidarProblemaAsync(resposta, HttpStatusCode.BadRequest, Refresh);
        ValidarSemCache(resposta);
    }

    [Theory]
    [InlineData("desconhecido")]
    [InlineData("expirado")]
    [InlineData("revogado")]
    [InlineData("inativo")]
    public async Task RefreshInvalidoOuUsuarioInativoRetornaMesmo401SemCriarSessao(string cenario)
    {
        var usuario = await GravarUsuarioAsync();
        await using var fabrica = CriarFabrica();
        using var cliente = fabrica.CreateClient();
        var login = await LoginAsync(cliente, usuario);
        await using (var contexto = banco.CriarContexto())
        {
            if (cenario == "inativo")
            {
                var entidade = await contexto.Usuarios.SingleAsync(item => item.Id == usuario.Id, Cancelamento);
                entidade.Inativar(DateTimeOffset.UtcNow);
            }
            else if (cenario == "revogado")
            {
                var token = await contexto.Tokens.SingleAsync(item => item.UsuarioId == usuario.Id, Cancelamento);
                token.Revogar(DateTimeOffset.UtcNow);
            }
            else if (cenario == "expirado")
            {
                await contexto.Tokens.Where(item => item.UsuarioId == usuario.Id).ExecuteUpdateAsync(
                    setters => setters.SetProperty(item => item.DataExpiracao, DateTimeOffset.UtcNow.AddMinutes(-1))
                        .SetProperty(item => item.DataCriacao, DateTimeOffset.UtcNow.AddDays(-1)), Cancelamento);
            }
            await contexto.SaveChangesAsync(Cancelamento);
        }
        var valor = cenario == "desconhecido" ? "CredencialSinteticaDesconhecidaE08" : login.RefreshToken;
        using var resposta = await RenovarAsync(cliente, valor);
        await ValidarProblemaAsync(resposta, HttpStatusCode.Unauthorized, Refresh);
        using var json = await LerJsonAsync(resposta);
        Assert.Equal("Renovação não autorizada.", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("Não foi possível renovar a sessão. Faça login novamente.", json.RootElement.GetProperty("detail").GetString());
        Assert.DoesNotContain(valor, json.RootElement.GetRawText(), StringComparison.Ordinal);
        ValidarSemCache(resposta);
        await using var leitura = banco.CriarContexto();
        Assert.Equal(1, await leitura.Tokens.CountAsync(item => item.UsuarioId == usuario.Id, Cancelamento));
    }

    [Fact]
    public async Task RenovacaoNoInstanteExatoDaExpiracaoERecusada()
    {
        var usuario = await GravarUsuarioAsync();
        var valor = "RefreshSinteticoExpiracaoE08";
        var agora = DateTimeOffset.UtcNow;
        await DadosPersistencia.GravarTokensAsync(banco, new Token(Guid.NewGuid(), usuario.Id, Hash(valor), agora.AddDays(-7), agora));
        await using var fabrica = new FabricaUsersApi(conexao: banco.Conexao,
            configurarServicos: servicos => servicos.AddSingleton<TimeProvider>(new RelogioFixo(agora)));
        using var cliente = fabrica.CreateClient();
        using var resposta = await RenovarAsync(cliente, valor);
        await ValidarProblemaAsync(resposta, HttpStatusCode.Unauthorized, Refresh);
    }

    [Fact]
    public async Task RefreshUsaDadosEPerfilAtuaisSemModificarClaimsDoAccessAnterior()
    {
        var usuario = await GravarUsuarioAsync();
        await using var fabrica = CriarFabrica();
        using var cliente = fabrica.CreateClient();
        var login = await LoginAsync(cliente, usuario);
        var emailNovo = $"atualizado-e08-{Guid.NewGuid():N}@exemplo.test";
        await using (var contexto = banco.CriarContexto())
        {
            var entidade = await contexto.Usuarios.SingleAsync(item => item.Id == usuario.Id, Cancelamento);
            entidade.AlterarPerfil(PerfisSistema.AdministradorId);
            entidade.AtualizarDados("Pessoa Atualizada E08", entidade.DataNascimento, emailNovo);
            await contexto.SaveChangesAsync(Cancelamento); // prepara cenário; as rotas administrativas entram na E09
        }
        using var resposta = await RenovarAsync(cliente, login.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var renovado = (await resposta.Content.ReadFromJsonAsync<RespostaLogin>(Cancelamento))!;
        Assert.Equal("Pessoa Atualizada E08", renovado.Usuario.Nome);
        Assert.Equal(emailNovo, renovado.Usuario.Email);
        Assert.Equal(PerfisSistema.AdministradorId, renovado.Usuario.PerfilId);
        Assert.Equal(PerfisSistema.Administrador, renovado.Usuario.Perfil);
        var leitor = new JwtSecurityTokenHandler();
        Assert.Equal(PerfisSistema.Administrador, leitor.ReadJwtToken(renovado.AccessToken).Claims.Single(item => item.Type == "role").Value);
        Assert.Equal(PerfisSistema.Usuario, leitor.ReadJwtToken(login.AccessToken).Claims.Single(item => item.Type == "role").Value);
        ValidarJwt(renovado, fabrica.Chaves);
    }

    [Fact]
    public async Task DuasRenovacoesHttpSimultaneasGeramApenasUmSucessor()
    {
        var usuario = await GravarUsuarioAsync();
        await using var fabrica = CriarFabrica();
        using var cliente = fabrica.CreateClient();
        var login = await LoginAsync(cliente, usuario);
        var respostas = await Task.WhenAll(RenovarAsync(cliente, login.RefreshToken), RenovarAsync(cliente, login.RefreshToken));
        try
        {
            Assert.Single(respostas, item => item.StatusCode == HttpStatusCode.OK);
            Assert.Single(respostas, item => item.StatusCode == HttpStatusCode.Unauthorized);
            await using var contexto = banco.CriarContexto();
            var tokens = await contexto.Tokens.Where(item => item.UsuarioId == usuario.Id).ToListAsync(Cancelamento);
            Assert.Equal(2, tokens.Count);
            Assert.Single(tokens, item => item.EstaRevogado());
            Assert.Single(tokens, item => !item.EstaRevogado());
        }
        finally { foreach (var resposta in respostas) resposta.Dispose(); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("nao-e-um-jwt")]
    public async Task LogoutExigeAccessTokenValidoAntesDeConsultarBanco(string? token)
    {
        await using var fabrica = new FabricaUsersApi();
        using var cliente = fabrica.CreateClient();
        using var resposta = await SairAsync(cliente, token);
        await ValidarProblemaAsync(resposta, HttpStatusCode.Unauthorized, Logout);
    }

    [Fact]
    public async Task LogoutRecusaJwtExpiradoSemRevogarSessao()
    {
        var usuario = await GravarUsuarioAsync();
        await using var fabrica = CriarFabrica();
        using var cliente = fabrica.CreateClient();
        var login = await LoginAsync(cliente, usuario);
        var vencido = JwtTeste.Criar(fabrica.Chaves, corpo =>
        {
            corpo["sub"] = usuario.Id.ToString();
            corpo["exp"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();
        });
        using var resposta = await SairAsync(cliente, vencido);
        await ValidarProblemaAsync(resposta, HttpStatusCode.Unauthorized, Logout);
        using var renovar = await RenovarAsync(cliente, login.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, renovar.StatusCode);
    }

    [Fact]
    public async Task LogoutRevogaTodasAsSessoesDoSubIgnoraIdDoCorpoENaoInvalidaJwtJaEmitido()
    {
        var usuario = await GravarUsuarioAsync();
        var outro = await GravarUsuarioAsync();
        using var logs = new ColetorLogs();
        await using var fabrica = CriarFabrica(logs);
        using var cliente = fabrica.CreateClient();
        var primeiro = await LoginAsync(cliente, usuario);
        var segundo = await LoginAsync(cliente, usuario);
        var deOutro = await LoginAsync(cliente, outro);
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, Logout)
        {
            Content = JsonContent.Create(new { usuarioId = outro.Id, refreshToken = deOutro.RefreshToken })
        };
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", primeiro.AccessToken);
        using var resposta = await cliente.SendAsync(requisicao, Cancelamento);
        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.Empty(await resposta.Content.ReadAsStringAsync(Cancelamento));
        ValidarSemCache(resposta);
        using var repetir = await SairAsync(cliente, primeiro.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, repetir.StatusCode); // mesmo JWT ainda autentica: idempotência
        using var renovarPrimeiro = await RenovarAsync(cliente, primeiro.RefreshToken);
        using var renovarSegundo = await RenovarAsync(cliente, segundo.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, renovarPrimeiro.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, renovarSegundo.StatusCode);
        using var renovarOutro = await RenovarAsync(cliente, deOutro.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, renovarOutro.StatusCode);
        await using var contexto = banco.CriarContexto();
        var tokens = await contexto.Tokens.Where(item => item.UsuarioId == usuario.Id).ToListAsync(Cancelamento);
        Assert.Equal(2, tokens.Count);
        Assert.All(tokens, item => Assert.NotNull(item.DataRevogacao));
        Assert.Equal(tokens[0].DataRevogacao, tokens[1].DataRevogacao);
        foreach (var segredo in new[] { primeiro.AccessToken, primeiro.RefreshToken, segundo.RefreshToken, deOutro.RefreshToken })
            Assert.DoesNotContain(segredo, logs.Texto, StringComparison.Ordinal);
        // Uma autenticação nova por senha pode abrir outra sessão após o logout.
        var novoLogin = await LoginAsync(cliente, usuario);
        using var novaRenovacao = await RenovarAsync(cliente, novoLogin.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, novaRenovacao.StatusCode);
    }

    [Theory]
    [InlineData(Refresh)]
    [InlineData(Logout)]
    public async Task BancoIndisponivelRetorna500GenericoSemExporCredencial(string rota)
    {
        using var logs = new ColetorLogs();
        await using var fabrica = new FabricaUsersApi(configurarServicos:
            servicos => servicos.AddLogging(logging => logging.AddProvider(logs)));
        using var cliente = fabrica.CreateClient();
        var token = JwtTeste.Criar(fabrica.Chaves);
        const string valor = "RefreshSinteticoFalhaBancoE08";
        using var resposta = rota == Refresh ? await RenovarAsync(cliente, valor) : await SairAsync(cliente, token);
        await ValidarProblemaAsync(resposta, HttpStatusCode.InternalServerError, rota);
        var corpo = await resposta.Content.ReadAsStringAsync(Cancelamento);
        foreach (var segredo in new[] { token, valor, Hash(valor) })
        {
            Assert.DoesNotContain(segredo, corpo, StringComparison.Ordinal);
            Assert.DoesNotContain(segredo, logs.Texto, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("Npgsql", corpo, StringComparison.Ordinal);
        ValidarSemCache(resposta);
    }

    [Fact]
    public async Task FalhaAoInserirSucessorReverteRevogacaoENaoEntregaTokensPermitindoNovaTentativa()
    {
        var usuario = await GravarUsuarioAsync();
        using var logs = new ColetorLogs();
        var observados = new RefreshObservado();
        await using var fabrica = new FabricaUsersApi(conexao: banco.Conexao, configurarServicos: servicos =>
        {
            servicos.AddLogging(logging => logging.AddProvider(logs));
            servicos.AddScoped<IServicoRefreshToken>(provider => new EmissorObservado(
                ActivatorUtilities.CreateInstance<ServicoRefreshToken>(provider), observados));
        });
        using var cliente = fabrica.CreateClient();
        var login = await LoginAsync(cliente, usuario);
        await using var contexto = banco.CriarContexto();
        var idAnterior = await contexto.Tokens.Where(item => item.UsuarioId == usuario.Id).Select(item => item.Id).SingleAsync(Cancelamento);
        var sql = $"ALTER TABLE \"tb_Tokens\" ADD CONSTRAINT \"CK_E08_FalhaSucessor\" CHECK (\"UsuarioId\" <> '{usuario.Id:D}'::uuid OR \"Id\" = '{idAnterior:D}'::uuid)";
        await contexto.Database.ExecuteSqlRawAsync(sql, Cancelamento);
        try
        {
            using var resposta = await RenovarAsync(cliente, login.RefreshToken);
            await ValidarProblemaAsync(resposta, HttpStatusCode.InternalServerError, Refresh);
            Assert.NotNull(observados.Ultimo);
            Assert.Null((await contexto.Tokens.AsNoTracking().SingleAsync(item => item.Id == idAnterior, Cancelamento)).DataRevogacao);
            Assert.Equal(1, await contexto.Tokens.CountAsync(item => item.UsuarioId == usuario.Id, Cancelamento));
            var corpo = await resposta.Content.ReadAsStringAsync(Cancelamento);
            foreach (var segredo in new[] { login.RefreshToken, observados.Ultimo.Valor, observados.Ultimo.Hash })
            {
                Assert.DoesNotContain(segredo, corpo, StringComparison.Ordinal);
                Assert.DoesNotContain(segredo, logs.Texto, StringComparison.Ordinal);
            }
            Assert.DoesNotContain("CK_E08_FalhaSucessor", corpo, StringComparison.Ordinal);
        }
        finally
        {
            await contexto.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"tb_Tokens\" DROP CONSTRAINT IF EXISTS \"CK_E08_FalhaSucessor\"", CancellationToken.None);
        }
        using var tentativa = await RenovarAsync(cliente, login.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, tentativa.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RefreshELogoutConcorrentesEmAmbasAsOrdensNaoDeixamSessaoRenovavel(bool refreshPrimeiro)
    {
        var usuario = await GravarUsuarioAsync();
        var interceptador = new PausaDepoisDaRevogacao();
        var nome = $"e08-concorrencia-{Guid.NewGuid():N}";
        var conexao = new NpgsqlConnectionStringBuilder(banco.Conexao) { ApplicationName = nome }.ConnectionString;
        await using var fabrica = new FabricaUsersApi(conexao: conexao, configurarServicos: servicos =>
            servicos.AddScoped(_ => new UsersDbContext(new DbContextOptionsBuilder<UsersDbContext>()
                .UseNpgsql(conexao).AddInterceptors(interceptador).Options)));
        using var cliente = fabrica.CreateClient();
        var login = await LoginAsync(cliente, usuario);
        var primeira = refreshPrimeiro ? RenovarAsync(cliente, login.RefreshToken) : SairAsync(cliente, login.AccessToken);
        try
        {
            await interceptador.Chegou.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancelamento);
            var segunda = refreshPrimeiro ? SairAsync(cliente, login.AccessToken) : RenovarAsync(cliente, login.RefreshToken);
            try
            {
                // Prova que a segunda requisição está esperando o bloqueio real no PostgreSQL.
                await EsperarBloqueioAsync(nome);
            }
            finally { interceptador.Liberar.TrySetResult(); }
            using var resultadoPrimeiro = await primeira;
            using var resultadoSegundo = await segunda;
            Assert.Equal(refreshPrimeiro ? HttpStatusCode.OK : HttpStatusCode.NoContent, resultadoPrimeiro.StatusCode);
            Assert.Equal(refreshPrimeiro ? HttpStatusCode.NoContent : HttpStatusCode.Unauthorized, resultadoSegundo.StatusCode);
            await using var contexto = banco.CriarContexto();
            var tokens = await contexto.Tokens.AsNoTracking().Where(item => item.UsuarioId == usuario.Id).ToListAsync(Cancelamento);
            Assert.Equal(refreshPrimeiro ? 2 : 1, tokens.Count);
            Assert.All(tokens, item => Assert.NotNull(item.DataRevogacao));
            if (refreshPrimeiro)
            {
                var par = (await resultadoPrimeiro.Content.ReadFromJsonAsync<RespostaLogin>(Cancelamento))!;
                using var repetido = await RenovarAsync(cliente, par.RefreshToken);
                Assert.Equal(HttpStatusCode.Unauthorized, repetido.StatusCode);
            }
        }
        finally { interceptador.Liberar.TrySetResult(); }
    }

    private async Task EsperarBloqueioAsync(string nome)
    {
        await using var conexao = new NpgsqlConnection(banco.Conexao);
        await conexao.OpenAsync(Cancelamento);
        await using var comando = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = @nome AND wait_event_type = 'Lock' AND query LIKE '%FOR UPDATE%')", conexao);
        comando.Parameters.AddWithValue("nome", nome);
        var tempo = Stopwatch.StartNew();
        while (tempo.Elapsed < TimeSpan.FromSeconds(10))
        {
            if ((bool)(await comando.ExecuteScalarAsync(Cancelamento))!) return;
            await Task.Delay(20, Cancelamento);
        }
        Assert.Fail("A segunda requisição não aguardou o bloqueio de sessões por usuário.");
    }

    private async Task<Usuario> GravarUsuarioAsync()
    {
        var usuario = new Usuario(Guid.NewGuid(), "Pessoa Sessão E08", DadosPersistencia.ProximoCpf(),
            new DateOnly(2000, 2, 29), $"e08-{Guid.NewGuid():N}@exemplo.test",
            new ServicoHashSenha().GerarHash(Senha), PerfisSistema.UsuarioId, DateTimeOffset.UtcNow);
        await using var contexto = banco.CriarContexto();
        contexto.Usuarios.Add(usuario);
        await contexto.SaveChangesAsync(Cancelamento);
        return usuario;
    }

    private FabricaUsersApi CriarFabrica(ColetorLogs? logs = null) => new(conexao: banco.Conexao,
        configurarServicos: logs is null ? null : servicos => servicos.AddLogging(logging => logging.AddProvider(logs)));

    private static async Task<RespostaLogin> LoginAsync(HttpClient cliente, Usuario usuario)
    {
        using var resposta = await cliente.PostAsJsonAsync("/api/v1/auth/login", new { email = usuario.Email, senha = Senha }, Cancelamento);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<RespostaLogin>(Cancelamento))!;
    }

    private static Task<HttpResponseMessage> RenovarAsync(HttpClient cliente, string valor) =>
        cliente.PostAsJsonAsync(Refresh, new { refreshToken = valor }, Cancelamento);

    private static async Task<HttpResponseMessage> SairAsync(HttpClient cliente, string? access)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, Logout);
        if (access is not null) requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return await cliente.SendAsync(requisicao, Cancelamento);
    }

    private static string Hash(string valor) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor)));

    private static void ValidarJwt(RespostaLogin par, ChavesJwtTeste chaves)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(par.AccessToken);
        Assert.Equal("RS256", jwt.Header.Alg);
        Assert.Equal(chaves.KeyId, jwt.Header.Kid);
        Assert.Equal(par.Usuario.Id.ToString(), jwt.Subject);
        Assert.Equal(par.Usuario.Perfil, jwt.Claims.Single(item => item.Type == "role").Value);
        Assert.Equal(par.ExpiresAt.ToUnixTimeSeconds(), jwt.Payload.Expiration);
        Assert.Equal(900, jwt.Payload.Expiration - new DateTimeOffset(jwt.Payload.IssuedAt).ToUnixTimeSeconds());
        Assert.DoesNotContain(jwt.Claims, item => item.Type is "email" or "name" or "nome" or "cpf");
        var partes = par.AccessToken.Split('.');
        using var publica = RSA.Create();
        publica.ImportFromPem(File.ReadAllText(chaves.Publica));
        var assinatura = partes[2].Replace('-', '+').Replace('_', '/');
        assinatura = assinatura.PadRight((assinatura.Length + 3) / 4 * 4, '=');
        Assert.True(publica.VerifyData(Encoding.ASCII.GetBytes(partes[0] + "." + partes[1]),
            Convert.FromBase64String(assinatura), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    private static async Task<JsonDocument> LerJsonAsync(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Cancelamento));

    private static async Task ValidarProblemaAsync(HttpResponseMessage resposta, HttpStatusCode esperado, string rota)
    {
        Assert.Equal(esperado, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        using var json = await LerJsonAsync(resposta);
        Assert.Equal((int)esperado, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(rota, json.RootElement.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
        Assert.False(json.RootElement.TryGetProperty("accessToken", out _));
        Assert.False(json.RootElement.TryGetProperty("refreshToken", out _));
        if (esperado == HttpStatusCode.Unauthorized)
            Assert.Contains(resposta.Headers.WwwAuthenticate, item => item.Scheme == "Bearer");
    }

    private static void ValidarSemCache(HttpResponseMessage resposta)
    {
        Assert.True(resposta.Headers.CacheControl?.NoStore);
        Assert.True(resposta.Headers.CacheControl?.NoCache);
    }

    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    private sealed class RefreshObservado
    {
        public RefreshTokenGerado? Ultimo { get; set; }
    }

    private sealed class EmissorObservado(ServicoRefreshToken emissor, RefreshObservado observado) : IServicoRefreshToken
    {
        public RefreshTokenGerado GerarToken()
        {
            var gerado = emissor.GerarToken();
            observado.Ultimo = gerado;
            return gerado;
        }
        public string CalcularHash(string valor) => emissor.CalcularHash(valor);
    }

    private sealed class PausaDepoisDaRevogacao : DbCommandInterceptor
    {
        private int _pausou;
        public TaskCompletionSource Chegou { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Liberar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE tb_Tokens", StringComparison.Ordinal)
                || command.CommandText.StartsWith("UPDATE \"tb_Tokens\"", StringComparison.Ordinal))
            {
                if (Interlocked.Exchange(ref _pausou, 1) == 0)
                {
                    Chegou.TrySetResult();
                    await Liberar.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
                }
            }
            return result;
        }
    }
}
