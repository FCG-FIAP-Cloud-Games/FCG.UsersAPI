using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using FCG.Users.Api.Contracts.Auth;
using FCG.Users.Domain.Entities;
using FCG.Users.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FCG.Users.IntegrationTests.Host;

[Collection(ColecaoBancoUsers.Nome)]
public sealed class TestesAdministracaoHttp(BancoUsersFixture banco)
{
    private static CancellationToken Cancelamento => DadosE09.Cancelamento;
    private FabricaUsersApi Fabrica(ColetorLogs? logs = null) => new(conexao: banco.Conexao,
        configurarServicos: logs is null ? null : servicos => servicos.AddLogging(logging => logging.AddProvider(logs)));

    [Fact]
    public async Task TitularConsultaPropriosDadosSemCpfNascimentoSenhaHashOuTokens()
    {
        var usuario = await DadosE09.GravarUsuarioAsync(banco);
        await using var fabrica = Fabrica();
        using var cliente = fabrica.CreateClient();
        using var resposta = await DadosE09.EnviarAsync(cliente, "GET", $"/api/v1/usuarios/{usuario.Id}", DadosE09.Jwt(fabrica, usuario.Id));
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.True(resposta.Headers.CacheControl?.NoStore);
        using var json = await DadosE09.JsonAsync(resposta);
        Assert.Equal(usuario.Id, json.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(usuario.Email, json.RootElement.GetProperty("email").GetString());
        Assert.Equal(7, json.RootElement.EnumerateObject().Count());
        foreach (var segredo in new[] { "cpf", "dataNascimento", "senha", "senhaHash", "accessToken", "refreshToken" })
            Assert.False(json.RootElement.TryGetProperty(segredo, out _));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task TitularNaoAcessaOutroIdMesmoSemBanco(string metodo)
    {
        await using var fabrica = new FabricaUsersApi();
        using var cliente = fabrica.CreateClient();
        using var resposta = await DadosE09.EnviarAsync(cliente, metodo, $"/api/v1/usuarios/{Guid.NewGuid()}",
            DadosE09.Jwt(fabrica, Guid.NewGuid()), metodo == "PUT" ? DadosE09.Edicao() : null);
        await DadosE09.ProblemaAsync(resposta, HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("GET", "id")]
    [InlineData("PUT", "id")]
    [InlineData("DELETE", "id")]
    [InlineData("PUT", "perfil")]
    [InlineData("POST", "administradores")]
    public async Task TodasAsOperacoesProtegidasExigemJwtAntesDoBanco(string metodo, string operacao)
    {
        await using var fabrica = new FabricaUsersApi();
        using var cliente = fabrica.CreateClient();
        var rota = Rota(operacao, Guid.NewGuid());
        using var resposta = await DadosE09.EnviarAsync(cliente, metodo, rota, corpo: Corpo(operacao, metodo));
        await DadosE09.ProblemaAsync(resposta, HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("DELETE", "id")]
    [InlineData("PUT", "perfil")]
    [InlineData("POST", "administradores")]
    public async Task PerfilComumNaoExecutaOperacoesAdministrativasNemNoProprioUsuario(string metodo, string operacao)
    {
        await using var fabrica = new FabricaUsersApi();
        using var cliente = fabrica.CreateClient();
        var id = Guid.NewGuid();
        using var resposta = await DadosE09.EnviarAsync(cliente, metodo, Rota(operacao, id), DadosE09.Jwt(fabrica, id), Corpo(operacao, metodo));
        await DadosE09.ProblemaAsync(resposta, HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TitularOuAdministradorEditaDadosSemAlterarCpfSenhaPerfilOuAtividade(bool administrador)
    {
        var usuario = await DadosE09.GravarUsuarioAsync(banco);
        using var logs = new ColetorLogs();
        await using var fabrica = Fabrica(logs);
        using var cliente = fabrica.CreateClient();
        var access = DadosE09.Jwt(fabrica, administrador ? Guid.NewGuid() : usuario.Id, administrador);
        var email = $"novo-{Guid.NewGuid():N}@exemplo.test";
        var corpo = new { nome = "  Nome   Atualizado  ", dataNascimento = "1999-05-10", email = $" {email.ToUpperInvariant()} ",
            cpf = "00000000000", senha = "SenhaDeInjecao#E09", senhaHash = "hash-injetado", perfilId = PerfisSistema.AdministradorId, ativo = false };
        using var resposta = await DadosE09.EnviarAsync(cliente, "PUT", $"/api/v1/usuarios/{usuario.Id}", access, corpo);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        await using var contexto = banco.CriarContexto();
        var persistido = await contexto.Usuarios.AsNoTracking().SingleAsync(item => item.Id == usuario.Id, Cancelamento);
        Assert.Equal("Nome Atualizado", persistido.Nome);
        Assert.Equal(email, persistido.Email);
        Assert.Equal(new DateOnly(1999, 5, 10), persistido.DataNascimento);
        Assert.Equal(usuario.CPF, persistido.CPF);
        Assert.Equal(usuario.SenhaHash, persistido.SenhaHash);
        Assert.Equal(usuario.PerfilId, persistido.PerfilId);
        Assert.True(persistido.Ativo);
        Assert.Null(persistido.DataInativacao);
        var log = Assert.Single(await contexto.LogsUsuarios.Where(item => item.UsuarioId == usuario.Id).ToListAsync(Cancelamento));
        Assert.Equal("Dados do usuário alterados.", log.Descricao);
        using var repetir = await DadosE09.EnviarAsync(cliente, "PUT", $"/api/v1/usuarios/{usuario.Id}", access, corpo);
        Assert.Equal(HttpStatusCode.OK, repetir.StatusCode);
        Assert.Equal(1, await contexto.LogsUsuarios.CountAsync(item => item.UsuarioId == usuario.Id, Cancelamento));
        using var obter = await DadosE09.EnviarAsync(cliente, "GET", $"/api/v1/usuarios/{usuario.Id}", access);
        Assert.Equal(HttpStatusCode.OK, obter.StatusCode);
        foreach (var segredo in new[] { access, usuario.CPF, usuario.SenhaHash, email, "SenhaDeInjecao#E09" })
            Assert.DoesNotContain(segredo, logs.Texto, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nome")]
    [InlineData("email")]
    [InlineData("nascimento")]
    [InlineData("futuro")]
    public async Task EdicaoInvalidaRetorna400AntesDoBanco(string campo)
    {
        await using var fabrica = new FabricaUsersApi();
        using var cliente = fabrica.CreateClient();
        var id = Guid.NewGuid();
        var corpo = new { nome = campo == "nome" ? "A" : "Pessoa E09", email = campo == "email" ? "invalido" : "pessoa@example.test",
            dataNascimento = campo == "nascimento" ? "0001-01-01" : campo == "futuro" ? "2999-01-01" : "2000-01-01" };
        using var resposta = await DadosE09.EnviarAsync(cliente, "PUT", $"/api/v1/usuarios/{id}", DadosE09.Jwt(fabrica, id), corpo);
        await DadosE09.ProblemaAsync(resposta, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EmailOcupadoRetorna409SemAlteracaoOuAuditoria()
    {
        var usuario = await DadosE09.GravarUsuarioAsync(banco);
        var outro = await DadosE09.GravarUsuarioAsync(banco);
        await using var fabrica = Fabrica();
        using var cliente = fabrica.CreateClient();
        using var resposta = await DadosE09.EnviarAsync(cliente, "PUT", $"/api/v1/usuarios/{usuario.Id}",
            DadosE09.Jwt(fabrica, usuario.Id), DadosE09.Edicao(outro.Email));
        await DadosE09.ProblemaAsync(resposta, HttpStatusCode.Conflict);
        await using var contexto = banco.CriarContexto();
        var persistido = await contexto.Usuarios.SingleAsync(item => item.Id == usuario.Id, Cancelamento);
        Assert.Equal(usuario.Email, persistido.Email);
        Assert.Equal(usuario.Nome, persistido.Nome);
        Assert.Empty(await contexto.LogsUsuarios.Where(item => item.UsuarioId == usuario.Id).ToListAsync(Cancelamento));
    }

    [Fact]
    public async Task EdicoesConcorrentesComMesmoEmailConfirmamUmUsuarioEUmLog()
    {
        var a = await DadosE09.GravarUsuarioAsync(banco);
        var b = await DadosE09.GravarUsuarioAsync(banco);
        var email = $"disputa-{Guid.NewGuid():N}@exemplo.test";
        await using var fabrica = Fabrica();
        using var cliente = fabrica.CreateClient();
        var respostas = await Task.WhenAll(
            DadosE09.EnviarAsync(cliente, "PUT", $"/api/v1/usuarios/{a.Id}", DadosE09.Jwt(fabrica, a.Id), DadosE09.Edicao(email)),
            DadosE09.EnviarAsync(cliente, "PUT", $"/api/v1/usuarios/{b.Id}", DadosE09.Jwt(fabrica, b.Id), DadosE09.Edicao(email)));
        try
        {
            Assert.Single(respostas, item => item.StatusCode == HttpStatusCode.OK);
            Assert.Single(respostas, item => item.StatusCode == HttpStatusCode.Conflict);
            await using var contexto = banco.CriarContexto();
            Assert.Equal(1, await contexto.Usuarios.CountAsync(item => item.Email == email, Cancelamento));
            Assert.Equal(1, await contexto.LogsUsuarios.CountAsync(item => item.UsuarioId == a.Id || item.UsuarioId == b.Id, Cancelamento));
        }
        finally { foreach (var resposta in respostas) resposta.Dispose(); }
    }

    [Fact]
    public async Task AdministradorCadastraOutroAdministradorComHashEAuditoriaImpondoPerfil()
    {
        await using var fabrica = Fabrica();
        using var cliente = fabrica.CreateClient();
        using var resposta = await DadosE09.EnviarAsync(cliente, "POST", "/api/v1/usuarios/administradores",
            DadosE09.Jwt(fabrica, Guid.NewGuid(), true), DadosE09.Cadastro());
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        using var json = await DadosE09.JsonAsync(resposta);
        var id = json.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(PerfisSistema.AdministradorId, json.RootElement.GetProperty("perfilId").GetGuid());
        await using var contexto = banco.CriarContexto();
        var usuario = await contexto.Usuarios.SingleAsync(item => item.Id == id, Cancelamento);
        Assert.True(new FCG.Users.Infrastructure.Security.ServicoHashSenha().Verificar(DadosE09.Senha, usuario.SenhaHash));
        Assert.Equal("Usuário cadastrado.", (await contexto.LogsUsuarios.SingleAsync(item => item.UsuarioId == id, Cancelamento)).Descricao);
        using var obter = await DadosE09.EnviarAsync(cliente, "GET", resposta.Headers.Location!.OriginalString, DadosE09.Jwt(fabrica, id, true));
        Assert.Equal(HttpStatusCode.OK, obter.StatusCode);
    }

    [Fact]
    public async Task TrocaDePerfilERefletidaNoRefreshEnquantoJwtAntigoConservaRole()
    {
        var usuario = await DadosE09.GravarUsuarioAsync(banco);
        await using var fabrica = Fabrica();
        using var cliente = fabrica.CreateClient();
        var login = await DadosE09.LoginAsync(cliente, usuario);
        var admin = DadosE09.Jwt(fabrica, Guid.NewGuid(), true);
        var rota = $"/api/v1/usuarios/{usuario.Id}/perfil";
        var corpo = new { perfilId = PerfisSistema.AdministradorId };
        using var alteracao = await DadosE09.EnviarAsync(cliente, "PUT", rota, admin, corpo);
        Assert.Equal(HttpStatusCode.OK, alteracao.StatusCode);
        using var repetir = await DadosE09.EnviarAsync(cliente, "PUT", rota, admin, corpo);
        Assert.Equal(HttpStatusCode.OK, repetir.StatusCode);
        using var antigo = await DadosE09.EnviarAsync(cliente, "PUT", rota, login.AccessToken, corpo);
        await DadosE09.ProblemaAsync(antigo, HttpStatusCode.Forbidden);
        using var refresh = await cliente.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.RefreshToken }, Cancelamento);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var novo = (await refresh.Content.ReadFromJsonAsync<RespostaLogin>(Cancelamento))!;
        Assert.Equal(PerfisSistema.Administrador, novo.Usuario.Perfil);
        Assert.Equal(PerfisSistema.Administrador, new JwtSecurityTokenHandler().ReadJwtToken(novo.AccessToken).Claims.Single(item => item.Type == "role").Value);
        Assert.Equal(PerfisSistema.Usuario, new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken).Claims.Single(item => item.Type == "role").Value);
        using var autorizado = await DadosE09.EnviarAsync(cliente, "PUT", rota, novo.AccessToken, corpo);
        Assert.Equal(HttpStatusCode.OK, autorizado.StatusCode);
        await using var contexto = banco.CriarContexto();
        Assert.Equal(1, await contexto.LogsUsuarios.CountAsync(item => item.UsuarioId == usuario.Id && item.Descricao == "Perfil do usuário alterado.", Cancelamento));
    }

    [Fact]
    public async Task PerfilDesconhecidoRetorna400SemSalvarLog()
    {
        var usuario = await DadosE09.GravarUsuarioAsync(banco);
        await using var fabrica = Fabrica();
        using var cliente = fabrica.CreateClient();
        using var resposta = await DadosE09.EnviarAsync(cliente, "PUT", $"/api/v1/usuarios/{usuario.Id}/perfil",
            DadosE09.Jwt(fabrica, Guid.NewGuid(), true), new { perfilId = Guid.NewGuid() });
        await DadosE09.ProblemaAsync(resposta, HttpStatusCode.BadRequest);
        await using var contexto = banco.CriarContexto();
        Assert.Equal(PerfisSistema.UsuarioId, (await contexto.Usuarios.SingleAsync(item => item.Id == usuario.Id, Cancelamento)).PerfilId);
        Assert.False(await contexto.LogsUsuarios.AnyAsync(item => item.UsuarioId == usuario.Id, Cancelamento));
    }

    [Theory]
    [InlineData("GET", "id")]
    [InlineData("PUT", "id")]
    [InlineData("DELETE", "id")]
    [InlineData("PUT", "perfil")]
    public async Task AdministradorRecebe404ParaUsuarioInexistente(string metodo, string operacao)
    {
        await using var fabrica = Fabrica();
        using var cliente = fabrica.CreateClient();
        using var resposta = await DadosE09.EnviarAsync(cliente, metodo, Rota(operacao, Guid.NewGuid()),
            DadosE09.Jwt(fabrica, Guid.NewGuid(), true), Corpo(operacao, metodo));
        await DadosE09.ProblemaAsync(resposta, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("GET", "id")]
    [InlineData("PUT", "id")]
    [InlineData("DELETE", "id")]
    [InlineData("PUT", "perfil")]
    public async Task IdVazioRetorna400SemBancoParaAdministrador(string metodo, string operacao)
    {
        await using var fabrica = new FabricaUsersApi();
        using var cliente = fabrica.CreateClient();
        using var resposta = await DadosE09.EnviarAsync(cliente, metodo, Rota(operacao, Guid.Empty),
            DadosE09.Jwt(fabrica, Guid.NewGuid(), true), Corpo(operacao, metodo));
        await DadosE09.ProblemaAsync(resposta, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InativacaoELogSaoPreservadosSemExclusaoFisicaEBloqueiamLoginERefresh()
    {
        var usuario = await DadosE09.GravarUsuarioAsync(banco);
        await using var fabrica = Fabrica();
        using var cliente = fabrica.CreateClient();
        var login = await DadosE09.LoginAsync(cliente, usuario);
        var admin = DadosE09.Jwt(fabrica, Guid.NewGuid(), true);
        var rota = $"/api/v1/usuarios/{usuario.Id}";
        using var resposta = await DadosE09.EnviarAsync(cliente, "DELETE", rota, admin);
        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.Empty(await resposta.Content.ReadAsStringAsync(Cancelamento));
        await using var contexto = banco.CriarContexto();
        var inativado = await contexto.Usuarios.AsNoTracking().SingleAsync(item => item.Id == usuario.Id, Cancelamento);
        Assert.False(inativado.Ativo);
        Assert.NotNull(inativado.DataInativacao);
        Assert.Equal(usuario.CPF, inativado.CPF);
        var log = await contexto.LogsUsuarios.SingleAsync(item => item.UsuarioId == usuario.Id, Cancelamento);
        Assert.Equal("Usuário inativado.", log.Descricao);
        Assert.Equal(inativado.DataInativacao, log.DataCriacao);
        using var repetir = await DadosE09.EnviarAsync(cliente, "DELETE", rota, admin);
        Assert.Equal(HttpStatusCode.NoContent, repetir.StatusCode);
        Assert.Equal(inativado.DataInativacao, (await contexto.Usuarios.AsNoTracking().SingleAsync(item => item.Id == usuario.Id, Cancelamento)).DataInativacao);
        Assert.Equal(1, await contexto.LogsUsuarios.CountAsync(item => item.UsuarioId == usuario.Id, Cancelamento));
        using var loginInativo = await cliente.PostAsJsonAsync("/api/v1/auth/login", new { email = usuario.Email, senha = DadosE09.Senha }, Cancelamento);
        using var refresh = await cliente.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.RefreshToken }, Cancelamento);
        Assert.Equal(HttpStatusCode.Unauthorized, loginInativo.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        using var obter = await DadosE09.EnviarAsync(cliente, "GET", rota, login.AccessToken);
        Assert.Equal(HttpStatusCode.OK, obter.StatusCode); // contrato aprovado: JWT anterior ainda é válido
    }

    [Fact]
    public async Task InativacoesSimultaneasGeramUmaUnicaAuditoria()
    {
        var usuario = await DadosE09.GravarUsuarioAsync(banco);
        await using var fabrica = Fabrica();
        using var cliente = fabrica.CreateClient();
        var access = DadosE09.Jwt(fabrica, Guid.NewGuid(), true);
        var rota = $"/api/v1/usuarios/{usuario.Id}";
        var respostas = await Task.WhenAll(DadosE09.EnviarAsync(cliente, "DELETE", rota, access), DadosE09.EnviarAsync(cliente, "DELETE", rota, access));
        try
        {
            Assert.All(respostas, item => Assert.Equal(HttpStatusCode.NoContent, item.StatusCode));
            await using var contexto = banco.CriarContexto();
            Assert.Equal(1, await contexto.LogsUsuarios.CountAsync(item => item.UsuarioId == usuario.Id, Cancelamento));
        }
        finally { foreach (var resposta in respostas) resposta.Dispose(); }
    }

    [Theory]
    [InlineData("dados")]
    [InlineData("perfil")]
    [InlineData("inativar")]
    public async Task FalhaDaAuditoriaRetorna500EReverteAlteracaoInteira(string operacao)
    {
        var usuario = await DadosE09.GravarUsuarioAsync(banco);
        using var logs = new ColetorLogs();
        await using var fabrica = Fabrica(logs);
        using var cliente = fabrica.CreateClient();
        var access = DadosE09.Jwt(fabrica, Guid.NewGuid(), true);
        var metodo = operacao == "inativar" ? "DELETE" : "PUT";
        var rota = $"/api/v1/usuarios/{usuario.Id}" + (operacao == "perfil" ? "/perfil" : "");
        var corpo = operacao == "inativar" ? null : operacao == "perfil" ? (object)new { perfilId = PerfisSistema.AdministradorId } : DadosE09.Edicao();
        await using var contexto = banco.CriarContexto();
        var sql = $"ALTER TABLE \"tb_LogUsuarios\" ADD CONSTRAINT \"CK_E09_FalhaAuditoria\" CHECK (\"UsuarioId\" <> '{usuario.Id:D}'::uuid)";
        await contexto.Database.ExecuteSqlRawAsync(sql, Cancelamento);
        try
        {
            using var resposta = await DadosE09.EnviarAsync(cliente, metodo, rota, access, corpo);
            await DadosE09.ProblemaAsync(resposta, HttpStatusCode.InternalServerError);
            var persistido = await contexto.Usuarios.AsNoTracking().SingleAsync(item => item.Id == usuario.Id, Cancelamento);
            Assert.Equal(usuario.Nome, persistido.Nome);
            Assert.Equal(usuario.Email, persistido.Email);
            Assert.Equal(usuario.PerfilId, persistido.PerfilId);
            Assert.True(persistido.Ativo);
            Assert.Null(persistido.DataInativacao);
            Assert.False(await contexto.LogsUsuarios.AnyAsync(item => item.UsuarioId == usuario.Id, Cancelamento));
            var texto = await resposta.Content.ReadAsStringAsync(Cancelamento);
            Assert.DoesNotContain("CK_E09_FalhaAuditoria", texto, StringComparison.Ordinal);
            foreach (var segredo in new[] { usuario.CPF, usuario.Email, usuario.SenhaHash, access })
            {
                Assert.DoesNotContain(segredo, texto, StringComparison.Ordinal);
                Assert.DoesNotContain(segredo, logs.Texto, StringComparison.Ordinal);
            }
        }
        finally { await contexto.Database.ExecuteSqlRawAsync("ALTER TABLE \"tb_LogUsuarios\" DROP CONSTRAINT IF EXISTS \"CK_E09_FalhaAuditoria\"", CancellationToken.None); }
        using var tentar = await DadosE09.EnviarAsync(cliente, metodo, rota, access, corpo);
        Assert.Equal(operacao == "inativar" ? HttpStatusCode.NoContent : HttpStatusCode.OK, tentar.StatusCode);
        Assert.Equal(1, await contexto.LogsUsuarios.CountAsync(item => item.UsuarioId == usuario.Id, Cancelamento));
    }

    [Theory]
    [InlineData("GET", "id")]
    [InlineData("PUT", "id")]
    [InlineData("DELETE", "id")]
    [InlineData("PUT", "perfil")]
    public async Task FalhaDeBancoNasOperacoesProtegidasRetorna500Generico(string metodo, string operacao)
    {
        await using var fabrica = new FabricaUsersApi();
        using var cliente = fabrica.CreateClient();
        using var resposta = await DadosE09.EnviarAsync(cliente, metodo, Rota(operacao, Guid.NewGuid()),
            DadosE09.Jwt(fabrica, Guid.NewGuid(), true), Corpo(operacao, metodo));
        await DadosE09.ProblemaAsync(resposta, HttpStatusCode.InternalServerError);
        Assert.DoesNotContain("Npgsql", await resposta.Content.ReadAsStringAsync(Cancelamento), StringComparison.Ordinal);
    }

    private static string Rota(string operacao, Guid id) => operacao == "administradores" ? "/api/v1/usuarios/administradores"
        : $"/api/v1/usuarios/{id}" + (operacao == "perfil" ? "/perfil" : "");
    private static object? Corpo(string operacao, string metodo) => operacao == "administradores" ? DadosE09.Cadastro()
        : operacao == "perfil" ? new { perfilId = PerfisSistema.AdministradorId }
        : metodo == "PUT" ? DadosE09.Edicao() : null;
}
