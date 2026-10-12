using FCG.Notifications.Application.Messaging.Contracts;
using System.Diagnostics;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FCG.Users.Api.Contracts.Auth;
using FCG.Users.Api.Contracts.Usuarios;
using FCG.Users.Domain.Entities;
using FCG.Users.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace FCG.Users.IntegrationTests.Acceptance;

[CollectionDefinition(Nome, DisableParallelization = true)]
public sealed class ColecaoPassagemE14
{
    public const string Nome = "E14 artefato publicado e recursos novos";
}

[Collection(ColecaoPassagemE14.Nome)]
public sealed class TestesPassagemE14(ITestOutputHelper saida)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;
    private static readonly string[] Auditorias = ["Dados do usuário alterados.", "Perfil do usuário alterado.", "Usuário cadastrado.", "Usuário inativado."];
    private static readonly string[] Criacoes = ["primeiro-admin-local", "cadastro-publico", "cadastro-administrativo"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Senha = "SenhaSintetica@E14";

    [Fact]
    public async Task ArtefatoPublicadoExecutaOsQuatroCardsEmAmbienteNovo()
    {
        var pasta = await ArtefatoUsersApiE14.PrepararAsync(Cancelamento);
        await using var banco = new BancoUsersFixture();
        await banco.InitializeAsync();
        await using var rabbit = new RabbitMqFixture();
        await rabbit.InitializeAsync();
        await using var cenario = await rabbit.NovoCenarioAsync();
        using var chaves = new ChavesJwtTeste(keyId: "passagem-e14");
        var emailAdmin = $"admin-e14-{Guid.NewGuid():N}@exemplo.test";
        var cpfAdmin = DadosPersistencia.ProximoCpf();
        Assert.Equal(0, await ArtefatoUsersApiE14.InicializarAdminAsync(pasta, banco.Conexao, cpfAdmin, emailAdmin, Senha, Cancelamento));
        // Uma segunda execução não duplica a conta, log ou evento, nem troca sua senha.
        Assert.Equal(0, await ArtefatoUsersApiE14.InicializarAdminAsync(pasta, banco.Conexao, cpfAdmin, emailAdmin, "OutraSenha@E14", Cancelamento));
        await using var api = await ProcessoUsersApiE13.IniciarPublicadoAsync(banco.Conexao, chaves, cenario.Configuracao, pasta, Cancelamento);
        var cliente = api.Cliente;
        var admin = await LoginAsync(cliente, emailAdmin);
        var correlacao = Guid.NewGuid();
        var email = $"pessoa-e14-{Guid.NewGuid():N}@exemplo.test";
        using var cadastro = new HttpRequestMessage(HttpMethod.Post, "/api/v1/usuarios")
        { Content = JsonContent.Create(new { nome = "Pessoa Sintética E14", cpf = DadosPersistencia.ProximoCpf(), dataNascimento = "2000-02-29", email, senha = Senha }) };
        cadastro.Headers.Add("X-Correlation-ID", correlacao.ToString("D"));
        using var criado = await cliente.SendAsync(cadastro, Cancelamento);
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
        Assert.Equal(correlacao.ToString("D"), Assert.Single(criado.Headers.GetValues("X-Correlation-ID")));
        var pessoa = (await criado.Content.ReadFromJsonAsync<RespostaUsuario>(Cancelamento))!;
        Assert.Equal(PerfisSistema.UsuarioId, pessoa.PerfilId);
        var login = await LoginAsync(cliente, email);
        ValidarPublicamente(login, chaves, PerfisSistema.Usuario);
        using (var anonimo = await cliente.GetAsync($"/api/v1/usuarios/{pessoa.Id}", Cancelamento))
            Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        using (var titular = await cliente.GetAsync($"/api/v1/usuarios/{pessoa.Id}", Cancelamento))
            Assert.Equal(HttpStatusCode.OK, titular.StatusCode);
        using (var proibido = await cliente.PutAsJsonAsync($"/api/v1/usuarios/{pessoa.Id}/perfil", new { perfilId = PerfisSistema.AdministradorId }, Cancelamento))
            Assert.Equal(HttpStatusCode.Forbidden, proibido.StatusCode);
        var emailAtualizado = $"atualizada-e14-{Guid.NewGuid():N}@exemplo.test";
        using (var atualizado = await cliente.PutAsJsonAsync($"/api/v1/usuarios/{pessoa.Id}",
            new { nome = "Pessoa Atualizada E14", dataNascimento = "2000-02-29", email = emailAtualizado }, Cancelamento))
            Assert.Equal(HttpStatusCode.OK, atualizado.StatusCode);
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        using (var perfil = await cliente.PutAsJsonAsync($"/api/v1/usuarios/{pessoa.Id}/perfil", new { perfilId = PerfisSistema.AdministradorId }, Cancelamento))
            Assert.Equal(HttpStatusCode.OK, perfil.StatusCode);
        using var renovacao = await cliente.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.RefreshToken }, Cancelamento);
        Assert.Equal(HttpStatusCode.OK, renovacao.StatusCode);
        var renovado = (await renovacao.Content.ReadFromJsonAsync<RespostaLogin>(Cancelamento))!;
        ValidarPublicamente(renovado, chaves, PerfisSistema.Administrador);
        Assert.Equal(PerfisSistema.Usuario, new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken).Claims.Single(item => item.Type == "role").Value);
        using (var reuso = await cliente.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.RefreshToken }, Cancelamento))
            Assert.Equal(HttpStatusCode.Unauthorized, reuso.StatusCode);
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", renovado.AccessToken);
        using (var logout = await cliente.PostAsync("/api/v1/auth/logout", null, Cancelamento))
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using (var revogado = await cliente.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = renovado.RefreshToken }, Cancelamento))
            Assert.Equal(HttpStatusCode.Unauthorized, revogado.StatusCode);
        using (var accessAindaValido = await cliente.GetAsync($"/api/v1/usuarios/{pessoa.Id}", Cancelamento))
            Assert.Equal(HttpStatusCode.OK, accessAindaValido.StatusCode);
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        using (var inativacao = await cliente.DeleteAsync($"/api/v1/usuarios/{pessoa.Id}", Cancelamento))
            Assert.Equal(HttpStatusCode.NoContent, inativacao.StatusCode);
        using (var inativo = await cliente.PostAsJsonAsync("/api/v1/auth/login", new { email = emailAtualizado, senha = Senha }, Cancelamento))
            Assert.Equal(HttpStatusCode.Unauthorized, inativo.StatusCode);
        using var outro = await cliente.PostAsJsonAsync("/api/v1/usuarios/administradores",
            new { nome = "Outro Administrador E14", cpf = DadosPersistencia.ProximoCpf(), dataNascimento = "2000-02-29", email = $"outro-e14-{Guid.NewGuid():N}@exemplo.test", senha = Senha }, Cancelamento);
        Assert.Equal(HttpStatusCode.Created, outro.StatusCode);
        var outroAdmin = (await outro.Content.ReadFromJsonAsync<RespostaUsuario>(Cancelamento))!;
        Assert.Equal(PerfisSistema.AdministradorId, outroAdmin.PerfilId);
        await AguardarPublicacoesAsync(banco);
        await using var contexto = banco.CriarContexto();
        Assert.Equal(3, await contexto.Usuarios.CountAsync(Cancelamento));
        Assert.Equal(6, await contexto.LogsUsuarios.CountAsync(Cancelamento));
        var logs = await contexto.LogsUsuarios.Where(item => item.UsuarioId == pessoa.Id).Select(item => item.Descricao).ToListAsync(Cancelamento);
        Assert.Equal(Auditorias.Order(StringComparer.Ordinal), logs.Order(StringComparer.Ordinal));
        var eventos = await contexto.MensagensOutbox.AsNoTracking().ToListAsync(Cancelamento);
        Assert.Equal(3, eventos.Count);
        Assert.All(eventos, item => { Assert.NotNull(item.PublishedAt); Assert.True(item.Attempts >= 1); });
        var eventoPessoa = Assert.Single(eventos, item => item.UserId == pessoa.Id);
        Assert.Equal(correlacao, eventoPessoa.CorrelationId);
        Assert.Equal(email, eventoPessoa.LerEvento().Email); // Snapshot do cadastro, mesmo após editar/inativar.
        var mensagens = await cenario.MensagensAsync();
        Assert.Equal(3, mensagens.GetArrayLength());
        foreach (var mensagem in mensagens.EnumerateArray())
        {
            using var envelope = JsonDocument.Parse(Convert.FromBase64String(mensagem.GetProperty("payload").GetString()!));
            var corpo = envelope.RootElement.GetProperty("message");
            var evento = Assert.Single(eventos, item => item.EventId == corpo.GetProperty("eventId").GetGuid());
            Assert.Equal(evento.EventId, Guid.Parse(envelope.RootElement.GetProperty("messageId").GetString()!, CultureInfo.InvariantCulture));
            Assert.Equal(evento.CorrelationId, corpo.GetProperty("correlationId").GetGuid());
            Assert.Equal(evento.LerEvento(), JsonSerializer.Deserialize<UserCreatedEvent>(corpo.GetRawText(), JsonOptions));
        }
        var tokens = await contexto.Tokens.AsNoTracking().Where(item => item.UsuarioId == pessoa.Id).ToListAsync(Cancelamento);
        Assert.Equal(2, tokens.Count);
        Assert.All(tokens, item => { Assert.NotNull(item.DataRevogacao); Assert.Equal(64, item.TokenHash.Length); });
        Assert.Contains(tokens, item => item.TokenHash == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(renovado.RefreshToken))));
        saida.WriteLine("E14-EVIDENCIA:" + JsonSerializer.Serialize(new
        {
            cenario = "passagem-artefato-publicado", http = "Kestrel e DLL publicada com dependências próprias",
            apiSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(pasta, "FCG.Users.Api.dll"), Cancelamento))),
            migrations = (await contexto.Database.GetAppliedMigrationsAsync(Cancelamento)).ToArray(),
            usuarios = 3, auditorias = 6, eventos = 3, mensagensNaFila = 3, rabbitMqVersao = rabbit.Versao,
            jwt = new { algoritmo = "RS256", validadoSomenteComChavePublica = true, perfilAnteriorPreservado = true, perfilNovoNaRenovacao = true },
            sessao = new { reusoRefresh = 401, refreshAposLogout = 401, accessAposLogout = 200, loginInativo = 401 },
            permissoes = new { semBearer = 401, titular = 200, usuarioTrocaPerfil = 403, administradorTrocaPerfil = 200 },
            criacoes = Criacoes,
            consumidorNotificationsExecutado = false, emailEnviado = false
        }));
    }

    private static async Task<RespostaLogin> LoginAsync(HttpClient cliente, string email)
    {
        using var resposta = await cliente.PostAsJsonAsync("/api/v1/auth/login", new { email, senha = Senha }, Cancelamento);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<RespostaLogin>(Cancelamento))!;
    }

    private static void ValidarPublicamente(RespostaLogin par, ChavesJwtTeste chaves, string perfil)
    {
        using var publica = RSA.Create();
        publica.ImportFromPem(File.ReadAllText(chaves.Publica));
        var leitor = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var claims = leitor.ValidateToken(par.AccessToken, new TokenValidationParameters
        {
            RequireSignedTokens = true, ValidateIssuerSigningKey = true, IssuerSigningKey = new RsaSecurityKey(publica) { KeyId = chaves.KeyId, CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false } },
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ValidateIssuer = true, ValidIssuer = "FIAP.CloudGames",
            ValidateAudience = true, ValidAudience = "FIAP.CloudGames.Api", ValidateLifetime = true, RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30), NameClaimType = "sub", RoleClaimType = "role"
        }, out var validado);
        var jwt = Assert.IsType<JwtSecurityToken>(validado);
        Assert.Equal(chaves.KeyId, jwt.Header.Kid);
        Assert.Equal("RS256", jwt.Header.Alg);
        Assert.Equal(par.Usuario.Id.ToString(), claims.FindFirst("sub")!.Value);
        Assert.Equal(perfil, claims.FindFirst("role")!.Value);
        Assert.DoesNotContain(claims.Claims, item => item.Type is "name" or "email" or "cpf");
    }

    private static async Task AguardarPublicacoesAsync(BancoUsersFixture banco)
    {
        var tempo = Stopwatch.StartNew();
        while (tempo.Elapsed < TimeSpan.FromSeconds(20))
        {
            await using var contexto = banco.CriarContexto();
            if (await contexto.MensagensOutbox.CountAsync(item => item.PublishedAt != null, Cancelamento) == 3) return;
            await Task.Delay(50, Cancelamento);
        }
        Assert.Fail("O artefato publicado não concluiu os três eventos no prazo da prova.");
    }
}
