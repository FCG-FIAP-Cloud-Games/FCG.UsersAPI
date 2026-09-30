using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FCG.Users.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FCG.Users.IntegrationTests.Host;

public sealed class TestesHostHttp
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task HealthRespondeSemBancoOuBroker(string ambiente)
    {
        await using var fabrica = new FabricaUsersApi(ambiente);
        using var cliente = fabrica.CreateClient();

        using var resposta = await cliente.GetAsync("/health", TestContext.Current.CancellationToken);
        using var json = await LerJsonAsync(resposta);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("application/json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task SwaggerDocumentaOEndpointDeHealthEmDesenvolvimento()
    {
        await using var fabrica = new FabricaUsersApi();
        using var cliente = fabrica.CreateClient();

        using var resposta = await cliente.GetAsync("/swagger/v1/swagger.json", TestContext.Current.CancellationToken);
        using var json = await LerJsonAsync(resposta);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("FCG UsersAPI", json.RootElement.GetProperty("info").GetProperty("title").GetString());
        var caminhos = json.RootElement.GetProperty("paths");
        Assert.Single(caminhos.EnumerateObject());
        var respostas = caminhos.GetProperty("/health").GetProperty("get").GetProperty("responses");
        Assert.True(respostas.TryGetProperty("200", out _));
        Assert.True(respostas.TryGetProperty("503", out _));
    }

    [Fact]
    public async Task SwaggerUiEstaDisponivelEmDesenvolvimento()
    {
        await using var fabrica = new FabricaUsersApi();
        using var cliente = fabrica.CreateClient();

        using var resposta = await cliente.GetAsync("/swagger/index.html", TestContext.Current.CancellationToken);
        var html = await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("text/html", resposta.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Swagger UI", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Production", true, "/swagger/index.html")]
    [InlineData("Production", true, "/swagger/v1/swagger.json")]
    [InlineData("Development", false, "/swagger/index.html")]
    [InlineData("Development", false, "/swagger/v1/swagger.json")]
    public async Task SwaggerRespeitaAmbienteEConfiguracao(string ambiente, bool habilitado, string caminho)
    {
        await using var fabrica = new FabricaUsersApi(ambiente, habilitado);
        using var cliente = fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var resposta = await cliente.GetAsync(caminho, TestContext.Current.CancellationToken);
        using var json = await LerJsonAsync(resposta);

        ValidarProblema(resposta, json, HttpStatusCode.NotFound, caminho);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task RotaInexistenteRetornaProblemDetailsComTraceId(string ambiente)
    {
        await using var fabrica = new FabricaUsersApi(ambiente);
        using var cliente = fabrica.CreateClient();

        using var resposta = await cliente.GetAsync("/rota-inexistente", TestContext.Current.CancellationToken);
        using var json = await LerJsonAsync(resposta);

        ValidarProblema(resposta, json, HttpStatusCode.NotFound, "/rota-inexistente");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task ExcecaoRetorna500SemExporDetalhesInternos(string ambiente)
    {
        await using var fabrica = new FabricaUsersApi(ambiente, incluirControllerTeste: true);
        using var cliente = fabrica.CreateClient();

        using var resposta = await cliente.GetAsync("/__testes/e03/falha", TestContext.Current.CancellationToken);
        using var json = await LerJsonAsync(resposta);

        ValidarProblema(resposta, json, HttpStatusCode.InternalServerError, "/__testes/e03/falha");
        Assert.Equal("Erro interno no servidor.", json.RootElement.GetProperty("title").GetString());
        Assert.DoesNotContain("detalhe-interno-simulado-e03", json.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", json.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidacaoDeControllerRetorna400ComErrosPorCampo()
    {
        await using var fabrica = new FabricaUsersApi(incluirControllerTeste: true);
        using var cliente = fabrica.CreateClient();

        using var resposta = await cliente.PostAsJsonAsync(
            "/__testes/e03/validacao", new { }, TestContext.Current.CancellationToken);
        using var json = await LerJsonAsync(resposta);

        ValidarProblema(resposta, json, HttpStatusCode.BadRequest, "/__testes/e03/validacao");
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("Nome", out _));
    }

    [Fact]
    public async Task JsonMalformadoRetorna400EmVezDe500()
    {
        await using var fabrica = new FabricaUsersApi(incluirControllerTeste: true);
        using var cliente = fabrica.CreateClient();
        using var conteudo = new StringContent("{", Encoding.UTF8, "application/json");

        using var resposta = await cliente.PostAsync(
            "/__testes/e03/validacao", conteudo, TestContext.Current.CancellationToken);
        using var json = await LerJsonAsync(resposta);

        ValidarProblema(resposta, json, HttpStatusCode.BadRequest, "/__testes/e03/validacao");
        Assert.NotEmpty(json.RootElement.GetProperty("errors").EnumerateObject());
    }

    [Fact]
    public async Task HostNormalNaoExpoeOControllerDeCenariosDeTeste()
    {
        await using var fabrica = new FabricaUsersApi("Production");
        using var cliente = fabrica.CreateClient();

        using var resposta = await cliente.GetAsync("/__testes/e03/falha", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    private static async Task<JsonDocument> LerJsonAsync(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    private static void ValidarProblema(
        HttpResponseMessage resposta, JsonDocument json, HttpStatusCode statusEsperado, string caminho)
    {
        Assert.Equal(statusEsperado, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.Equal((int)statusEsperado, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(caminho, json.RootElement.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
    }
}
