using System.Security.Cryptography;
using System.Text;
using FCG.Users.Application.Usuarios;
using FCG.Users.Domain.Entities;
using FCG.Users.Infrastructure;
using FCG.Users.Infrastructure.Data;
using FCG.Users.Infrastructure.Repositories;
using FCG.Users.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FCG.Users.DemoSetup;

internal sealed class PreparadorDemonstracao
{
    private const string DiretorioChaves = "/data/jwt";
    private const string CaminhoPrivada = "/data/jwt/private.pem";
    private const string CaminhoPublica = "/data/jwt/public.pem";
    private const string IdentificadorChave = "users-demo-v1";
    // Exclusivo deste banco de demonstração: coordena inclusive geração de chaves e migrations.
    private const long IdentificadorLock = 84645159015001;
    private readonly IConfiguration _configuracao;
    private readonly ConfiguracaoJwt _configuracaoJwt;
    private readonly string? _caminhoExportacaoPublica;

    public PreparadorDemonstracao(IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        ValidarAmbiente(configuracao);
        _configuracao = configuracao;
        _configuracaoJwt = ConfiguracaoJwt.Criar(configuracao);
        ValidarConfiguracaoChaves(_configuracaoJwt);
        _caminhoExportacaoPublica = ExportadorChavePublica.ValidarCaminho(configuracao["Jwt:PublicKeyExport:Path"]);
    }

    public async Task ExecutarAsync(CancellationToken tokenCancelamento)
    {
        var servicos = new ServiceCollection();
        // Esta conexão mantém um lock de sessão: não retorna ao pool com um lock pendente.
        var conexaoPreparacao = new NpgsqlConnectionStringBuilder(_configuracao.GetConnectionString("UsersDatabase"))
        {
            Pooling = false
        };
        var configuracaoPersistencia = new ConfigurationBuilder()
            .AddConfiguration(_configuracao)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:UsersDatabase"] = conexaoPreparacao.ConnectionString
            }).Build();
        servicos.AdicionarPersistencia(configuracaoPersistencia);
        servicos.AdicionarSegurancaCadastros();
        servicos.AddSingleton(TimeProvider.System);
        servicos.AddScoped<ManipuladorCriarUsuario>();
        servicos.AddScoped<InicializadorAdministradorLocal>();

        await using var provedor = servicos.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        await using var escopo = provedor.CreateAsyncScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<UsersDbContext>();
        await contexto.Database.OpenConnectionAsync(tokenCancelamento);
        var lockObtido = false;
        try
        {
            await contexto.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_lock({IdentificadorLock})", tokenCancelamento);
            lockObtido = true;
            tokenCancelamento.ThrowIfCancellationRequested();

            PrepararChaves(tokenCancelamento);
            Console.WriteLine("Par de chaves JWT da demonstração pronto e validado.");

            await contexto.Database.MigrateAsync(tokenCancelamento);
            Console.WriteLine("Migrations da demonstração aplicadas; dados existentes preservados.");

            var inicializador = escopo.ServiceProvider.GetRequiredService<InicializadorAdministradorLocal>();
            var resultado = await inicializador.ExecutarAsync(new ComandoCriarUsuario(
                "Administrador Demonstração", "52998224725", new DateOnly(1990, 1, 1),
                "admin@fcg.local", "Admin_Demo_2026!", PerfisSistema.AdministradorId), tokenCancelamento);
            Console.WriteLine(resultado switch
            {
                StatusInicializacaoAdministrador.Criado => "Administrador sintético criado com auditoria e evento na Outbox.",
                StatusInicializacaoAdministrador.JaExistente => "Administrador existente preservado; nenhuma senha ou evento foi recriado.",
                _ => "Não foi possível preparar o administrador sintético. Verifique conflitos no banco da demonstração."
            });
            if (resultado is not (StatusInicializacaoAdministrador.Criado or StatusInicializacaoAdministrador.JaExistente))
                throw new InvalidOperationException("Não foi possível preparar o administrador sintético.");

            if (_caminhoExportacaoPublica is not null)
            {
                ExportadorChavePublica.Exportar(CaminhoPublica, _caminhoExportacaoPublica, tokenCancelamento);
                Console.WriteLine("Cópia exclusiva da chave pública pronta no volume de integração.");
            }

            Console.WriteLine("Preparação da demonstração concluída.");
        }
        finally
        {
            if (lockObtido)
            {
                // O token do usuário pode estar cancelado. Use um prazo próprio para liberar a sessão.
                using var liberarLock = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await contexto.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT pg_advisory_unlock({IdentificadorLock})", liberarLock.Token);
                }
                catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or OperationCanceledException)
                {
                    // Fechar a conexão também desfaz o lock de sessão no PostgreSQL.
                    Console.Error.WriteLine("Não foi possível confirmar a liberação do lock; a conexão será encerrada.");
                }
            }
            await contexto.Database.CloseConnectionAsync();
        }
    }

    private static void ValidarAmbiente(IConfiguration configuracao)
    {
        if (!OperatingSystem.IsLinux()
            || configuracao["DOTNET_ENVIRONMENT"] != "Development"
            || (configuracao["ASPNETCORE_ENVIRONMENT"] is { } ambienteAspNet
                && ambienteAspNet != "Development")
            || configuracao["DOTNET_RUNNING_IN_CONTAINER"] != "true"
            || configuracao["DemonstracaoLocal:Habilitada"] != "true")
            throw new InvalidOperationException("A preparação exige o contêiner Linux de demonstração em Development.");

        var conexao = new NpgsqlConnectionStringBuilder(configuracao.GetConnectionString("UsersDatabase"));
        if (conexao.Host != "postgres" || conexao.Database != "fcg_users_demo")
            throw new InvalidOperationException("A preparação aceita somente o banco específico da demonstração.");
    }

    private static void ValidarConfiguracaoChaves(ConfiguracaoJwt configuracao)
    {
        if (configuracao.ActiveKeyId != IdentificadorChave
            || configuracao.PrivateKeyPath != CaminhoPrivada
            || configuracao.ValidationKeys.Count != 1
            || configuracao.ValidationKeys[0].KeyId != IdentificadorChave
            || configuracao.ValidationKeys[0].PublicKeyPath != CaminhoPublica)
            throw new InvalidOperationException("A demonstração exige o par de chaves e o identificador definidos no Compose.");
    }

    private void PrepararChaves(CancellationToken tokenCancelamento)
    {
        tokenCancelamento.ThrowIfCancellationRequested();
        RecusarLink(DiretorioChaves);
        RecusarLink(CaminhoPrivada);
        RecusarLink(CaminhoPublica);
        var privadaExiste = File.Exists(CaminhoPrivada);
        var publicaExiste = File.Exists(CaminhoPublica);
        if (privadaExiste != publicaExiste)
            throw new InvalidOperationException("O par de chaves está incompleto. Nenhum arquivo existente será substituído.");

        if (!privadaExiste)
        {
            Directory.CreateDirectory(DiretorioChaves);
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(DiretorioChaves, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using var rsa = RSA.Create(2048);
            GravarNovoPem(CaminhoPrivada, rsa.ExportPkcs8PrivateKeyPem(), privada: true);
            GravarNovoPem(CaminhoPublica, rsa.ExportSubjectPublicKeyInfoPem(), privada: false);
        }

        // Reutiliza a validação da API: chave RSA >= 2048 e componentes públicos correspondentes.
        using var chaves = new ChavesJwtRsa(_configuracaoJwt);
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(DiretorioChaves, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(CaminhoPrivada, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.SetUnixFileMode(CaminhoPublica, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
        tokenCancelamento.ThrowIfCancellationRequested();
    }

    private static void GravarNovoPem(string caminho, string pem, bool privada)
    {
        var opcoes = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (OperatingSystem.IsLinux())
        {
            opcoes.UnixCreateMode = privada
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite
                : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        }
        using var arquivo = new FileStream(caminho, opcoes);
        using var escritor = new StreamWriter(arquivo, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        escritor.WriteLine(pem);
    }

    private static void RecusarLink(string caminho)
    {
        if ((File.Exists(caminho) || Directory.Exists(caminho))
            && (File.GetAttributes(caminho) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Arquivos e diretório de chaves não podem ser links simbólicos.");
    }
}
