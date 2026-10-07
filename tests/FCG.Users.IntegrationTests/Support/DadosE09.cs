using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FCG.Users.Api.Contracts.Auth;
using FCG.Users.Domain.Entities;
using FCG.Users.Infrastructure.Security;

namespace FCG.Users.IntegrationTests.Support;

internal static class DadosE09
{
    public const string Senha = "SenhaSintetica#E09";
    public static CancellationToken Cancelamento => TestContext.Current.CancellationToken;
    public static async Task<Usuario> GravarUsuarioAsync(BancoUsersFixture banco, bool administrador = false)
    {
        var usuario = new Usuario(Guid.NewGuid(), "Pessoa E09", DadosPersistencia.ProximoCpf(), new DateOnly(2000, 2, 29),
            $"e09-{Guid.NewGuid():N}@exemplo.test", new ServicoHashSenha().GerarHash(Senha),
            administrador ? PerfisSistema.AdministradorId : PerfisSistema.UsuarioId, DateTimeOffset.UtcNow);
        await using var contexto = banco.CriarContexto();
        contexto.Usuarios.Add(usuario);
        await contexto.SaveChangesAsync(Cancelamento);
        return usuario;
    }
    public static string Jwt(FabricaUsersApi fabrica, Guid id, bool administrador = false) => JwtTeste.Criar(fabrica.Chaves, claims =>
    {
        claims["sub"] = id.ToString();
        claims["role"] = administrador ? PerfisSistema.Administrador : PerfisSistema.Usuario;
    });
    public static async Task<HttpResponseMessage> EnviarAsync(HttpClient cliente, string metodo, string rota, string? access = null, object? corpo = null)
    {
        using var requisicao = new HttpRequestMessage(new HttpMethod(metodo), rota);
        if (access is not null) requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        if (corpo is not null) requisicao.Content = JsonContent.Create(corpo);
        return await cliente.SendAsync(requisicao, Cancelamento);
    }
    public static async Task<RespostaLogin> LoginAsync(HttpClient cliente, Usuario usuario)
    {
        using var resposta = await cliente.PostAsJsonAsync("/api/v1/auth/login", new { email = usuario.Email, senha = Senha }, Cancelamento);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<RespostaLogin>(Cancelamento))!;
    }
    public static async Task<JsonDocument> JsonAsync(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Cancelamento));
    public static async Task ProblemaAsync(HttpResponseMessage resposta, HttpStatusCode esperado)
    {
        Assert.Equal(esperado, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        using var json = await JsonAsync(resposta);
        Assert.Equal((int)esperado, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
        foreach (var segredo in new[] { "senha", "senhaHash", "cpf", "accessToken", "refreshToken" })
            Assert.False(json.RootElement.TryGetProperty(segredo, out _));
    }
    public static object Edicao(string? email = null) => new { nome = "Pessoa Alterada E09", dataNascimento = "1999-05-10", email = email ?? $"alterado-{Guid.NewGuid():N}@exemplo.test" };
    public static object Cadastro() => new { nome = "Novo Administrador E09", cpf = DadosPersistencia.ProximoCpf(), dataNascimento = "2000-05-10",
        email = $"novo-admin-{Guid.NewGuid():N}@exemplo.test", senha = Senha, perfilId = PerfisSistema.UsuarioId };
}
