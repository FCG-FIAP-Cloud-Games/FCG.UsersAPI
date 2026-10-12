using System.Diagnostics;
using FCG.Users.Api.Configuration;
using FCG.Users.Application.Usuarios;
using FCG.Users.Domain.Entities;
using FCG.Users.Infrastructure;
using FCG.Users.Infrastructure.Repositories;
using FCG.Users.Infrastructure.Security;
using FCG.Users.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace FCG.Users.IntegrationTests.Host;

public sealed class TestesInicializacaoAdministrador
{
    private static CancellationToken Cancelamento => DadosE09.Cancelamento;

    [Theory]
    [InlineData("Production", "127.0.0.1", false)]
    [InlineData("Staging", "localhost", false)]
    [InlineData("Development", "banco-remoto.example.test", false)]
    [InlineData("Development", "127.0.0.1", true)]
    [InlineData("Development", "localhost", true)]
    [InlineData("Development", "::1", true)]
    public void ComandoLocalSoAceitaDevelopmentEBancoEmLoopback(string ambiente, string host, bool permitido)
    {
        var configuracao = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:UsersDatabase"] = $"Host={host};Database=fcg_users;Username=teste;Password=SegredoSinteticoE09" }).Build();
        var contexto = new AmbienteTeste { EnvironmentName = ambiente };
        if (permitido) InicializacaoAdministradorLocal.ValidarAmbiente(configuracao, contexto);
        else
        {
            var erro = Assert.Throws<InvalidOperationException>(() => InicializacaoAdministradorLocal.ValidarAmbiente(configuracao, contexto));
            Assert.DoesNotContain("SegredoSinteticoE09", erro.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DuasInicializacoesCriamUmAdministradorEAuditoriaSemAlterarSenhaNaRepeticao()
    {
        await using var banco = new BancoUsersFixture();
        await banco.InitializeAsync();
        await using var servicos = CriarServicos(banco);
        await using var a = servicos.CreateAsyncScope();
        await using var b = servicos.CreateAsyncScope();
        var resultados = await Task.WhenAll(
            a.ServiceProvider.GetRequiredService<InicializadorAdministradorLocal>().ExecutarAsync(Comando(), Cancelamento),
            b.ServiceProvider.GetRequiredService<InicializadorAdministradorLocal>().ExecutarAsync(Comando(), Cancelamento));
        Assert.Single(resultados, item => item == StatusInicializacaoAdministrador.Criado);
        Assert.Single(resultados, item => item == StatusInicializacaoAdministrador.JaExistente);
        await using var contexto = banco.CriarContexto();
        var usuario = Assert.Single(await contexto.Usuarios.ToListAsync(Cancelamento));
        Assert.Equal(PerfisSistema.AdministradorId, usuario.PerfilId);
        Assert.True(new ServicoHashSenha().Verificar(DadosE09.Senha, usuario.SenhaHash));
        var hashAnterior = usuario.SenhaHash;
        Assert.Equal("Usuário cadastrado.", Assert.Single(await contexto.LogsUsuarios.ToListAsync(Cancelamento)).Descricao);
        await using var repetir = servicos.CreateAsyncScope();
        Assert.Equal(StatusInicializacaoAdministrador.JaExistente,
            await repetir.ServiceProvider.GetRequiredService<InicializadorAdministradorLocal>().ExecutarAsync(Comando() with { Senha = "OutraSenha#E09" }, Cancelamento));
        Assert.Equal(hashAnterior, (await contexto.Usuarios.AsNoTracking().SingleAsync(Cancelamento)).SenhaHash);
        Assert.Equal(1, await contexto.LogsUsuarios.CountAsync(Cancelamento));
    }

    [Fact]
    public async Task FalhaDaAuditoriaNaInicializacaoNaoDeixaPrimeiroAdministradorSemHistorico()
    {
        await using var banco = new BancoUsersFixture();
        await banco.InitializeAsync();
        await using var servicos = CriarServicos(banco);
        await using var contexto = banco.CriarContexto();
        await contexto.Database.ExecuteSqlRawAsync("ALTER TABLE \"tb_LogUsuarios\" ADD CONSTRAINT \"CK_E09_FalhaBootstrap\" CHECK (false)", Cancelamento);
        try
        {
            await using var escopo = servicos.CreateAsyncScope();
            await Assert.ThrowsAsync<DbUpdateException>(() => escopo.ServiceProvider.GetRequiredService<InicializadorAdministradorLocal>().ExecutarAsync(Comando(), Cancelamento));
            Assert.False(await contexto.Usuarios.AnyAsync(Cancelamento));
            Assert.False(await contexto.LogsUsuarios.AnyAsync(Cancelamento));
        }
        finally { await contexto.Database.ExecuteSqlRawAsync("ALTER TABLE \"tb_LogUsuarios\" DROP CONSTRAINT IF EXISTS \"CK_E09_FalhaBootstrap\"", CancellationToken.None); }
    }

    [Fact]
    public async Task ProgramaInicializaAdministradorSemChavesJwtESemIniciarHttp()
    {
        await using var banco = new BancoUsersFixture();
        await banco.InitializeAsync();
        var comando = Comando();
        var criado = await ExecutarProgramaAsync(banco, comando);
        Assert.Equal(0, criado.Codigo);
        Assert.Contains("Primeiro administrador local criado", criado.Saida, StringComparison.Ordinal);
        var repetido = await ExecutarProgramaAsync(banco, comando with { Senha = "SenhaNaoAplicada#E09" });
        Assert.Equal(0, repetido.Codigo);
        Assert.Contains("existe administrador", repetido.Saida, StringComparison.Ordinal);
        foreach (var execucao in new[] { criado, repetido })
        {
            Assert.DoesNotContain("Now listening", execucao.Saida, StringComparison.Ordinal);
            Assert.DoesNotContain(comando.Senha, execucao.Saida + execucao.Erros, StringComparison.Ordinal);
            Assert.DoesNotContain(comando.Email, execucao.Saida + execucao.Erros, StringComparison.Ordinal);
        }
        await using var contexto = banco.CriarContexto();
        var usuario = Assert.Single(await contexto.Usuarios.ToListAsync(Cancelamento));
        Assert.Equal(PerfisSistema.AdministradorId, usuario.PerfilId);
        Assert.True(new ServicoHashSenha().Verificar(comando.Senha, usuario.SenhaHash));
        Assert.Single(await contexto.LogsUsuarios.ToListAsync(Cancelamento));
    }

    private static async Task<(int Codigo, string Saida, string Erros)> ExecutarProgramaAsync(
        BancoUsersFixture banco, ComandoCriarUsuario comando)
    {
        // Usa o runtime/deps do projeto de testes: funciona em Debug e Release sem caminho da máquina.
        var assemblyTestes = typeof(TestesInicializacaoAdministrador).Assembly.Location;
        var inicio = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (var argumento in new[] { "exec", "--runtimeconfig", Path.ChangeExtension(assemblyTestes, ".runtimeconfig.json"),
            "--depsfile", Path.ChangeExtension(assemblyTestes, ".deps.json"), typeof(global::Program).Assembly.Location,
            "--inicializar-admin-local" }) inicio.ArgumentList.Add(argumento);
        foreach (var nome in inicio.Environment.Keys.Where(nome => nome.StartsWith("Jwt__", StringComparison.OrdinalIgnoreCase)).ToArray())
            inicio.Environment.Remove(nome);
        inicio.Environment["DOTNET_ENVIRONMENT"] = "Development";
        inicio.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        inicio.Environment["ConnectionStrings__UsersDatabase"] = banco.Conexao;
        inicio.Environment["AdministradorLocal__Nome"] = comando.Nome;
        inicio.Environment["AdministradorLocal__CPF"] = comando.CPF;
        inicio.Environment["AdministradorLocal__DataNascimento"] = comando.DataNascimento.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        inicio.Environment["AdministradorLocal__Email"] = comando.Email;
        inicio.Environment["AdministradorLocal__Senha"] = comando.Senha;
        using var processo = new Process { StartInfo = inicio };
        Assert.True(processo.Start());
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(Cancelamento);
        prazo.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var saida = processo.StandardOutput.ReadToEndAsync(prazo.Token);
            var erros = processo.StandardError.ReadToEndAsync(prazo.Token);
            await processo.WaitForExitAsync(prazo.Token);
            return (processo.ExitCode, await saida, await erros);
        }
        finally { if (!processo.HasExited) processo.Kill(entireProcessTree: true); }
    }

    private static ComandoCriarUsuario Comando() => new("Administrador Local", DadosPersistencia.ProximoCpf(), new DateOnly(2000, 1, 1),
        $"admin-e09-{Guid.NewGuid():N}@exemplo.test", DadosE09.Senha, PerfisSistema.AdministradorId);
    private static ServiceProvider CriarServicos(BancoUsersFixture banco)
    {
        var configuracao = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:UsersDatabase"] = banco.Conexao }).Build();
        var servicos = new ServiceCollection();
        servicos.AdicionarPersistencia(configuracao);
        servicos.AdicionarSegurancaCadastros();
        servicos.AddSingleton(TimeProvider.System);
        servicos.AddScoped<ManipuladorCriarUsuario>();
        servicos.AddScoped<InicializadorAdministradorLocal>();
        return servicos.BuildServiceProvider();
    }
    private sealed class AmbienteTeste : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "FCG.Users.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
