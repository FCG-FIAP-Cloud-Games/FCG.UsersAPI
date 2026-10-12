using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FCG.Users.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FCG.Users.IntegrationTests.Host;

public sealed class TestesAutorizacaoJwtHttp
{
    private const string Rota = "/__testes/e07/autenticado";
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TokenValidoAutenticaComSubERoleSemMapeamentoImplicito()
    {
        await using var fabrica = new FabricaUsersApi(incluirControllerTeste: true);
        using var cliente = fabrica.CreateClient();
        var id = Guid.NewGuid().ToString();
        var token = JwtTeste.Criar(fabrica.Chaves, corpo => corpo["sub"] = id);
        using var resposta = await EnviarAsync(cliente, token);
        using var json = await LerJsonAsync(resposta);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(id, json.RootElement.GetProperty("sub").GetString());
        Assert.Equal("Usuario", json.RootElement.GetProperty("role").GetString());
        Assert.Equal(id, json.RootElement.GetProperty("nomeIdentidade").GetString());
    }

    [Theory]
    [InlineData("Usuario", 403)]
    [InlineData("Administrador", 200)]
    public async Task PermissaoAdministrativaDistingueTokenValidoSemPermissao(string perfil, int esperado)
    {
        await using var fabrica = new FabricaUsersApi(incluirControllerTeste: true);
        using var cliente = fabrica.CreateClient();
        var token = JwtTeste.Criar(fabrica.Chaves, corpo => corpo["role"] = perfil);
        using var resposta = await EnviarAsync(cliente, token, "/__testes/e07/administrador");
        Assert.Equal((HttpStatusCode)esperado, resposta.StatusCode);
        if (esperado == 403)
        {
            using var json = await LerJsonAsync(resposta);
            ValidarProblema(resposta, json, HttpStatusCode.Forbidden, "/__testes/e07/administrador");
        }
    }

    [Theory]
    [InlineData("ausente")]
    [InlineData("malformado")]
    [InlineData("assinatura")]
    [InlineData("kid-ausente")]
    [InlineData("kid-desconhecido")]
    [InlineData("kid-caixa-diferente")]
    [InlineData("HS256")]
    [InlineData("RS512")]
    [InlineData("none")]
    [InlineData("issuer")]
    [InlineData("issuer-ausente")]
    [InlineData("audience")]
    [InlineData("audience-ausente")]
    [InlineData("expirado")]
    [InlineData("futuro")]
    [InlineData("exp-ausente")]
    [InlineData("sub-ausente")]
    [InlineData("sub-invalido")]
    [InlineData("sub-vazio")]
    [InlineData("sub-duplicado")]
    [InlineData("role-ausente")]
    [InlineData("role-invalido")]
    [InlineData("role-duplicado")]
    [InlineData("iat-ausente")]
    [InlineData("iat-invalido")]
    [InlineData("iat-negativo")]
    [InlineData("iat-zero")]
    [InlineData("iat-duplicado")]
    [InlineData("jti-ausente")]
    [InlineData("jti-vazio")]
    [InlineData("jti-duplicado")]
    public async Task TokenInvalidoRecebe401SemVazarTokenNosLogsOuResposta(string cenario)
    {
        using var logs = new ColetorLogs();
        await using var fabrica = new FabricaUsersApi(incluirControllerTeste: true,
            configurarServicos: servicos => servicos.AddLogging(logging => logging.AddProvider(logs)));
        using var cliente = fabrica.CreateClient();
        using var outraChave = new ChavesJwtTeste();
        var token = cenario switch
        {
            "ausente" => null,
            "malformado" => "token-malformado-e07-segredo",
            "assinatura" => JwtTeste.Criar(outraChave, kid: fabrica.Chaves.KeyId),
            "kid-ausente" => JwtTeste.Criar(fabrica.Chaves, omitirKid: true),
            "kid-desconhecido" => JwtTeste.Criar(fabrica.Chaves, kid: "desconhecida"),
            "kid-caixa-diferente" => JwtTeste.Criar(fabrica.Chaves, kid: fabrica.Chaves.KeyId.ToUpperInvariant()),
            "HS256" or "RS512" or "none" => JwtTeste.Criar(fabrica.Chaves, algoritmo: cenario),
            _ => JwtTeste.Criar(fabrica.Chaves, corpo => AlterarClaims(corpo, cenario))
        };
        using var resposta = await EnviarAsync(cliente, token);
        using var json = await LerJsonAsync(resposta);
        ValidarProblema(resposta, json, HttpStatusCode.Unauthorized, Rota);
        Assert.Contains(resposta.Headers.WwwAuthenticate, cabecalho => cabecalho.Scheme == "Bearer");
        Assert.DoesNotContain("IDX", json.RootElement.GetRawText(), StringComparison.Ordinal);
        if (token is not null)
        {
            Assert.DoesNotContain(token, logs.Texto, StringComparison.Ordinal);
            Assert.DoesNotContain(token, json.RootElement.GetRawText(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("exp")]
    [InlineData("nbf")]
    public async Task ToleranciaDeRelogioAceitaDiferencaMenorQueTrintaSegundos(string claim)
    {
        await using var fabrica = new FabricaUsersApi(incluirControllerTeste: true);
        using var cliente = fabrica.CreateClient();
        var token = JwtTeste.Criar(fabrica.Chaves, corpo =>
        {
            var agora = DateTimeOffset.UtcNow;
            corpo["nbf"] = agora.AddMinutes(-2).ToUnixTimeSeconds();
            corpo[claim] = agora.AddSeconds(claim == "exp" ? -10 : 10).ToUnixTimeSeconds();
        });
        using var resposta = await EnviarAsync(cliente, token);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Fact]
    public async Task ReiniciarHostComOsMesmosArquivosPreservaValidacaoDoToken()
    {
        using var chaves = new ChavesJwtTeste();
        var token = JwtTeste.Criar(chaves);
        await using (var primeira = new FabricaUsersApi(incluirControllerTeste: true, chaves: chaves))
        {
            using var cliente = primeira.CreateClient();
            using var resposta = await EnviarAsync(cliente, token);
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        }
        await using var segunda = new FabricaUsersApi(incluirControllerTeste: true, chaves: chaves);
        using var segundoCliente = segunda.CreateClient();
        using var segundaResposta = await EnviarAsync(segundoCliente, token);
        Assert.Equal(HttpStatusCode.OK, segundaResposta.StatusCode);
    }

    [Fact]
    public async Task RotacaoAceitaChaveAnteriorEnquantoSuaPublicaEstaConfigurada()
    {
        using var anterior = new ChavesJwtTeste(keyId: "anterior");
        using var atual = new ChavesJwtTeste(keyId: "atual");
        var configuracao = new Dictionary<string, string?>
        {
            ["Jwt:Validation:Keys:1:KeyId"] = anterior.KeyId,
            ["Jwt:Validation:Keys:1:PublicKeyPath"] = anterior.Publica
        };
        await using (var fabrica = new FabricaUsersApi(incluirControllerTeste: true, chaves: atual,
            sobrescreverConfiguracao: configuracao))
        {
            using var cliente = fabrica.CreateClient();
            foreach (var chaves in new[] { anterior, atual })
            {
                using var resposta = await EnviarAsync(cliente, JwtTeste.Criar(chaves));
                Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            }
        }
        await using var semAnterior = new FabricaUsersApi(incluirControllerTeste: true, chaves: atual);
        using var outroCliente = semAnterior.CreateClient();
        using var rejeitada = await EnviarAsync(outroCliente, JwtTeste.Criar(anterior));
        Assert.Equal(HttpStatusCode.Unauthorized, rejeitada.StatusCode);
    }

    [Fact]
    public async Task RotaProtegidaDeTesteNaoExisteNoHostNormal()
    {
        await using var fabrica = new FabricaUsersApi();
        using var cliente = fabrica.CreateClient();
        using var resposta = await cliente.GetAsync(Rota, Cancelamento);
        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    private static void AlterarClaims(Dictionary<string, object?> corpo, string cenario)
    {
        switch (cenario)
        {
            case "issuer": corpo["iss"] = "outro-emissor"; break;
            case "issuer-ausente": corpo.Remove("iss"); break;
            case "audience": corpo["aud"] = "outra-api"; break;
            case "audience-ausente": corpo.Remove("aud"); break;
            case "expirado": corpo["nbf"] = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
                corpo["exp"] = DateTimeOffset.UtcNow.AddMinutes(-2).ToUnixTimeSeconds(); break;
            case "futuro": corpo["nbf"] = DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds(); break;
            case "exp-ausente": corpo.Remove("exp"); break;
            case "sub-ausente": corpo.Remove("sub"); break;
            case "sub-invalido": corpo["sub"] = "usuario-sem-guid"; break;
            case "sub-vazio": corpo["sub"] = Guid.Empty.ToString(); break;
            case "sub-duplicado": corpo["sub"] = new[] { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() }; break;
            case "role-ausente": corpo.Remove("role"); break;
            case "role-invalido": corpo["role"] = "SuperAdministrador"; break;
            case "role-duplicado": corpo["role"] = new[] { "Usuario", "Administrador" }; break;
            case "iat-ausente": corpo.Remove("iat"); break;
            case "iat-invalido": corpo["iat"] = "nao-numerico"; break;
            case "iat-negativo": corpo["iat"] = -1; break;
            case "iat-zero": corpo["iat"] = 0; break;
            case "iat-duplicado": corpo["iat"] = new[] { 1, 2 }; break;
            case "jti-ausente": corpo.Remove("jti"); break;
            case "jti-vazio": corpo["jti"] = " "; break;
            case "jti-duplicado": corpo["jti"] = new[] { "primeiro", "segundo" }; break;
            default: throw new ArgumentOutOfRangeException(nameof(cenario));
        }
    }

    private static async Task<HttpResponseMessage> EnviarAsync(HttpClient cliente, string? token, string rota = Rota)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Get, rota);
        if (token is not null) requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await cliente.SendAsync(requisicao, Cancelamento);
    }

    private static async Task<JsonDocument> LerJsonAsync(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Cancelamento));

    private static void ValidarProblema(HttpResponseMessage resposta, JsonDocument json, HttpStatusCode esperado, string rota)
    {
        Assert.Equal(esperado, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.Equal((int)esperado, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(rota, json.RootElement.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
    }
}
