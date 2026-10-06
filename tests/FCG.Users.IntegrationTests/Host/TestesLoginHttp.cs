using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FCG.Users.Application.Abstractions.Security;
using FCG.Users.Domain.Entities;
using FCG.Users.Infrastructure.Security;
using FCG.Users.IntegrationTests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FCG.Users.IntegrationTests.Host;

[Collection(ColecaoBancoUsers.Nome)]
public sealed class TestesLoginHttp(BancoUsersFixture banco)
{
    private const string Rota = "/api/v1/auth/login";
    private const string SenhaTeste = "SenhaLogin@42";
    private static readonly string[] ClaimsEsperadas = ["aud", "exp", "iat", "iss", "jti", "nbf", "role", "sub"];
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CadastroSeguidoDeLoginNormalizaEmailEmiteJwtAssinadoEGravaApenasHashDoRefresh()
    {
        using var logs = new ColetorLogs();
        await using var fabrica = CriarFabrica(logs);
        using var cliente = fabrica.CreateClient();
        var email = $"e07-{Guid.NewGuid():N}@exemplo.test";
        using var cadastro = await cliente.PostAsJsonAsync("/api/v1/usuarios", new
        {
            nome = "Pessoa Login E07", cpf = DadosPersistencia.ProximoCpf(), dataNascimento = "2000-02-29",
            email, senha = SenhaTeste
        }, Cancelamento);
        Assert.Equal(HttpStatusCode.Created, cadastro.StatusCode);
        using var cadastroJson = await LerJsonAsync(cadastro);
        var id = cadastroJson.RootElement.GetProperty("id").GetGuid();
        var antes = DateTimeOffset.UtcNow;
        using var resposta = await cliente.PostAsJsonAsync(Rota, new
        {
            email = $"  {email.ToUpperInvariant()}  ", senha = SenhaTeste,
            perfil = "Administrador", perfilId = PerfisSistema.AdministradorId
        }, Cancelamento);
        var depois = DateTimeOffset.UtcNow;
        using var json = await LerJsonAsync(resposta);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        ValidarSemCache(resposta);
        Assert.Equal("application/json", resposta.Content.Headers.ContentType?.MediaType);
        var raiz = json.RootElement;
        var accessToken = raiz.GetProperty("accessToken").GetString()!;
        var refreshToken = raiz.GetProperty("refreshToken").GetString()!;
        Assert.Equal("Bearer", raiz.GetProperty("tokenType").GetString());
        Assert.Equal(900, raiz.GetProperty("expiresIn").GetInt64());
        var expiraEm = raiz.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange(expiraEm, antes.AddSeconds(899), depois.AddMinutes(15));
        var usuarioJson = raiz.GetProperty("usuario");
        Assert.Equal(id, usuarioJson.GetProperty("id").GetGuid());
        Assert.Equal(email, usuarioJson.GetProperty("email").GetString());
        Assert.Equal("Pessoa Login E07", usuarioJson.GetProperty("nome").GetString());
        Assert.Equal(PerfisSistema.UsuarioId, usuarioJson.GetProperty("perfilId").GetGuid());
        Assert.Equal(PerfisSistema.Usuario, usuarioJson.GetProperty("perfil").GetString());
        Assert.Equal(5, usuarioJson.EnumerateObject().Count());
        ValidarJwtEmitido(accessToken, fabrica.Chaves, id, PerfisSistema.Usuario, expiraEm);
        Assert.Equal(86, refreshToken.Length);
        Assert.DoesNotContain('.', refreshToken);
        await using var contexto = banco.CriarContexto();
        var usuario = await contexto.Usuarios.SingleAsync(item => item.Id == id, Cancelamento);
        var token = Assert.Single(await contexto.Tokens.Where(item => item.UsuarioId == id).ToListAsync(Cancelamento));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken))), token.TokenHash);
        Assert.NotEqual(refreshToken, token.TokenHash);
        Assert.InRange(token.DataCriacao, antes.AddMilliseconds(-1), depois);
        Assert.Equal(TimeSpan.FromDays(7), token.DataExpiracao - token.DataCriacao);
        Assert.Null(token.DataRevogacao);
        Assert.Equal(1, await contexto.LogsUsuarios.CountAsync(item => item.UsuarioId == id, Cancelamento));
        foreach (var segredo in new[] { SenhaTeste, usuario.SenhaHash, usuario.CPF, accessToken, refreshToken, token.TokenHash, email })
            Assert.DoesNotContain(segredo, logs.Texto, StringComparison.Ordinal);
        foreach (var segredo in new[] { SenhaTeste, usuario.SenhaHash, usuario.CPF, token.TokenHash })
            Assert.DoesNotContain(segredo, raiz.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LoginMantemPerfilDoBancoEAceitaHashPbkdf2EIdentity(bool administrador, bool identity)
    {
        var usuario = await GravarUsuarioAsync(administrador: administrador, identity: identity);
        await using var fabrica = CriarFabrica();
        using var cliente = fabrica.CreateClient();
        using var resposta = await cliente.PostAsJsonAsync(Rota, new { email = usuario.Email, senha = SenhaTeste }, Cancelamento);
        using var json = await LerJsonAsync(resposta);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var perfil = administrador ? PerfisSistema.Administrador : PerfisSistema.Usuario;
        var raiz = json.RootElement;
        Assert.Equal(perfil, raiz.GetProperty("usuario").GetProperty("perfil").GetString());
        ValidarJwtEmitido(raiz.GetProperty("accessToken").GetString()!, fabrica.Chaves, usuario.Id, perfil,
            raiz.GetProperty("expiresAt").GetDateTimeOffset());
        await using var contexto = banco.CriarContexto();
        Assert.Equal(1, await contexto.Tokens.CountAsync(item => item.UsuarioId == usuario.Id, Cancelamento));
        Assert.Equal(usuario.SenhaHash, (await contexto.Usuarios.SingleAsync(item => item.Id == usuario.Id, Cancelamento)).SenhaHash);
    }

    [Theory]
    [InlineData("ausente")]
    [InlineData("inativo")]
    [InlineData("senha-incorreta")]
    public async Task CredenciaisRecusadasRetornamMesmo401SemCriarRefresh(string cenario)
    {
        var usuario = await GravarUsuarioAsync(inativo: cenario == "inativo");
        using var logs = new ColetorLogs();
        await using var fabrica = CriarFabrica(logs);
        using var cliente = fabrica.CreateClient();
        var email = cenario == "ausente" ? $"ausente-{Guid.NewGuid():N}@exemplo.test" : usuario.Email;
        var senha = cenario == "senha-incorreta" ? "SenhaIncorreta@42" : SenhaTeste;
        await using var contexto = banco.CriarContexto();
        var quantidadeAntes = await contexto.Tokens.CountAsync(Cancelamento);
        using var resposta = await cliente.PostAsJsonAsync(Rota, new { email, senha }, Cancelamento);
        using var json = await LerJsonAsync(resposta);
        ValidarProblema(resposta, json, HttpStatusCode.Unauthorized);
        Assert.Equal("Credenciais inválidas.", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("E-mail ou senha inválidos.", json.RootElement.GetProperty("detail").GetString());
        Assert.Equal("Bearer", Assert.Single(resposta.Headers.WwwAuthenticate).Scheme);
        Assert.Equal(quantidadeAntes, await contexto.Tokens.CountAsync(Cancelamento));
        foreach (var segredo in new[] { email, senha, usuario.SenhaHash })
        {
            Assert.DoesNotContain(segredo, json.RootElement.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(segredo, logs.Texto, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"email\":\"invalido\",\"senha\":\"SenhaLogin@42\"}")]
    [InlineData("{\"email\":\"pessoa@exemplo.test\",\"senha\":\"  \"}")]
    [InlineData("{\"email\":null,\"senha\":\"SenhaLogin@42\"}")]
    [InlineData("{\"email\":\"pessoa@exemplo.test\",\"senha\":null}")]
    [InlineData("{\"email\":42,\"senha\":\"SenhaLogin@42\"}")]
    [InlineData("{\"email\":\"pessoa@exemplo.test\",\"senha\":42}")]
    public async Task CorpoInvalidoRetorna400SemCacheSemTokenPersistido(string corpo)
    {
        using var logs = new ColetorLogs();
        await using var fabrica = CriarFabrica(logs);
        using var cliente = fabrica.CreateClient();
        await using var contexto = banco.CriarContexto();
        var quantidadeAntes = await contexto.Tokens.CountAsync(Cancelamento);
        using var conteudo = new StringContent(corpo, Encoding.UTF8, "application/json");
        using var resposta = await cliente.PostAsync(Rota, conteudo, Cancelamento);
        using var json = await LerJsonAsync(resposta);
        ValidarProblema(resposta, json, HttpStatusCode.BadRequest);
        Assert.NotEmpty(json.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal(quantidadeAntes, await contexto.Tokens.CountAsync(Cancelamento));
        Assert.DoesNotContain(SenhaTeste, json.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(SenhaTeste, logs.Texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FalhaDeBancoRetorna500GenericoSemCredenciaisNaResposta()
    {
        var conexao = new NpgsqlConnectionStringBuilder(banco.Conexao) { Database = "banco_inexistente_e07" };
        await using var fabrica = new FabricaUsersApi("Production", conexao: conexao.ConnectionString);
        using var cliente = fabrica.CreateClient();
        using var resposta = await cliente.PostAsJsonAsync(Rota,
            new { email = "login@exemplo.test", senha = SenhaTeste }, Cancelamento);
        using var json = await LerJsonAsync(resposta);
        ValidarProblema(resposta, json, HttpStatusCode.InternalServerError);
        foreach (var segredo in new[] { SenhaTeste, conexao.Password!, "banco_inexistente_e07", "Npgsql", "SELECT", "SenhaHash" })
            Assert.DoesNotContain(segredo, json.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FalhaAoPersistirRefreshRetorna500SemTokensNoBancoNaRespostaOuNosLogs()
    {
        var usuario = await GravarUsuarioAsync();
        var observados = new TokensObservados();
        using var logs = new ColetorLogs();
        await using var fabrica = new FabricaUsersApi(conexao: banco.Conexao, configurarServicos: servicos =>
        {
            servicos.AddLogging(logging => logging.AddProvider(logs));
            // Os geradores reais continuam sendo usados; o teste observa os valores apenas em memória.
            servicos.AddScoped<IServicoTokenJwt>(provider => new EmissorJwtObservado(
                ActivatorUtilities.CreateInstance<ServicoTokenJwt>(provider), observados));
            servicos.AddScoped<IServicoRefreshToken>(provider => new EmissorRefreshObservado(
                ActivatorUtilities.CreateInstance<ServicoRefreshToken>(provider), observados));
        });
        using var cliente = fabrica.CreateClient();
        await using var contexto = banco.CriarContexto();
        var quantidadeAntes = await contexto.Tokens.CountAsync(Cancelamento);
        // Guid criado pelo teste, nunca texto recebido pela API: a constraint afeta só este usuário.
        var criarRestricao = $"ALTER TABLE \"tb_Tokens\" ADD CONSTRAINT \"CK_E07_FalhaRefreshHttp\" CHECK (\"UsuarioId\" <> '{usuario.Id:D}'::uuid)";
        await contexto.Database.ExecuteSqlRawAsync(criarRestricao, Cancelamento);
        try
        {
            using var resposta = await cliente.PostAsJsonAsync(Rota,
                new { email = usuario.Email, senha = SenhaTeste }, Cancelamento);
            using var json = await LerJsonAsync(resposta);
            ValidarProblema(resposta, json, HttpStatusCode.InternalServerError);
            Assert.NotNull(observados.AccessToken);
            Assert.NotNull(observados.Refresh);
            Assert.Equal(quantidadeAntes, await contexto.Tokens.CountAsync(Cancelamento));
            Assert.False(await contexto.Tokens.AnyAsync(item => item.UsuarioId == usuario.Id, Cancelamento));
            Assert.True(await contexto.Usuarios.AnyAsync(item => item.Id == usuario.Id, Cancelamento));
            foreach (var segredo in new[] { SenhaTeste, usuario.Email, usuario.SenhaHash, observados.AccessToken,
                observados.Refresh.Valor, observados.Refresh.Hash })
            {
                Assert.DoesNotContain(segredo, json.RootElement.GetRawText(), StringComparison.Ordinal);
                Assert.DoesNotContain(segredo, logs.Texto, StringComparison.Ordinal);
            }
            Assert.Contains("CK_E07_FalhaRefreshHttp", logs.Texto, StringComparison.Ordinal);
            Assert.DoesNotContain("CK_E07_FalhaRefreshHttp", json.RootElement.GetRawText(), StringComparison.Ordinal);
        }
        finally
        {
            await contexto.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"tb_Tokens\" DROP CONSTRAINT IF EXISTS \"CK_E07_FalhaRefreshHttp\"", CancellationToken.None);
        }
    }

    private FabricaUsersApi CriarFabrica(ColetorLogs? logs = null) => new(conexao: banco.Conexao,
        configurarServicos: logs is null ? null : servicos => servicos.AddLogging(logging => logging.AddProvider(logs)));

    private async Task<Usuario> GravarUsuarioAsync(bool administrador = false, bool identity = false, bool inativo = false)
    {
        var hash = identity
            ? new PasswordHasher<object>().HashPassword(new object(), SenhaTeste)
            : new HashSenhaPbkdf2().Criar(SenhaTeste);
        var usuario = new Usuario(Guid.NewGuid(), "Pessoa Login E07", DadosPersistencia.ProximoCpf(), new DateOnly(2000, 2, 29),
            $"e07-{Guid.NewGuid():N}@exemplo.test", hash,
            administrador ? PerfisSistema.AdministradorId : PerfisSistema.UsuarioId, DateTimeOffset.UtcNow);
        if (inativo) usuario.Inativar(DateTimeOffset.UtcNow);
        await using var contexto = banco.CriarContexto();
        contexto.Usuarios.Add(usuario);
        await contexto.SaveChangesAsync(Cancelamento);
        return usuario;
    }

    private static void ValidarJwtEmitido(string token, ChavesJwtTeste chaves, Guid usuarioId, string perfil, DateTimeOffset expiraEm)
    {
        var partes = token.Split('.');
        Assert.Equal(3, partes.Length);
        using var cabecalho = JsonDocument.Parse(DecodificarBase64Url(partes[0]));
        using var corpo = JsonDocument.Parse(DecodificarBase64Url(partes[1]));
        Assert.Equal("RS256", cabecalho.RootElement.GetProperty("alg").GetString());
        Assert.Equal(chaves.KeyId, cabecalho.RootElement.GetProperty("kid").GetString());
        using var publica = RSA.Create();
        publica.ImportFromPem(File.ReadAllText(chaves.Publica));
        Assert.True(publica.VerifyData(Encoding.ASCII.GetBytes(partes[0] + "." + partes[1]),
            DecodificarBase64Url(partes[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var payload = corpo.RootElement;
        var nomes = payload.EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(ClaimsEsperadas, nomes);
        Assert.Equal(usuarioId.ToString(), payload.GetProperty("sub").GetString());
        Assert.Equal(perfil, payload.GetProperty("role").GetString());
        Assert.Equal("FIAP.CloudGames", payload.GetProperty("iss").GetString());
        Assert.Equal("FIAP.CloudGames.Api", payload.GetProperty("aud").GetString());
        Assert.Equal(expiraEm.ToUnixTimeSeconds(), payload.GetProperty("exp").GetInt64());
        Assert.Equal(900, payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64());
        Assert.Equal(payload.GetProperty("iat").GetInt64(), payload.GetProperty("nbf").GetInt64());
        Assert.True(Guid.TryParse(payload.GetProperty("jti").GetString(), out var jti) && jti != Guid.Empty);
    }

    private static byte[] DecodificarBase64Url(string valor)
    {
        var base64 = valor.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
    }

    private static async Task<JsonDocument> LerJsonAsync(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Cancelamento));

    private static void ValidarProblema(HttpResponseMessage resposta, JsonDocument json, HttpStatusCode esperado)
    {
        Assert.Equal(esperado, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.Equal((int)esperado, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(Rota, json.RootElement.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
        Assert.False(json.RootElement.TryGetProperty("accessToken", out _));
        Assert.False(json.RootElement.TryGetProperty("refreshToken", out _));
        ValidarSemCache(resposta);
    }

    private static void ValidarSemCache(HttpResponseMessage resposta)
    {
        Assert.True(resposta.Headers.CacheControl?.NoStore);
        Assert.True(resposta.Headers.CacheControl?.NoCache);
        Assert.Contains(resposta.Headers.Pragma, item => item.Name == "no-cache");
    }

    private sealed class TokensObservados
    {
        public string? AccessToken { get; set; }
        public RefreshTokenGerado? Refresh { get; set; }
    }

    private sealed class EmissorJwtObservado(ServicoTokenJwt emissor, TokensObservados observados) : IServicoTokenJwt
    {
        public TokenJwtGerado GerarToken(Usuario usuario, string perfil)
        {
            var token = emissor.GerarToken(usuario, perfil);
            observados.AccessToken = token.AccessToken;
            return token;
        }
    }

    private sealed class EmissorRefreshObservado(ServicoRefreshToken emissor, TokensObservados observados) : IServicoRefreshToken
    {
        public RefreshTokenGerado GerarToken()
        {
            var token = emissor.GerarToken();
            observados.Refresh = token;
            return token;
        }

        public string CalcularHash(string token) => emissor.CalcularHash(token);
    }
}
