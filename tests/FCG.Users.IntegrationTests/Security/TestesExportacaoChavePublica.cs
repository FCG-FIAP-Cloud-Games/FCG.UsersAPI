using System.Security.Cryptography;
using FCG.Users.DemoSetup;

namespace FCG.Users.IntegrationTests.Security;

public sealed class TestesExportacaoChavePublica : IDisposable
{
    private readonly string _diretorio = Path.Combine(Path.GetTempPath(), "fcg-publica-" + Guid.NewGuid().ToString("N"));

    public TestesExportacaoChavePublica() => Directory.CreateDirectory(_diretorio);

    [Fact]
    public void ExportacaoAusenteMantemCompatibilidade() =>
        Assert.Null(ExportadorChavePublica.ValidarCaminho(null));

    [Fact]
    public void AceitaSomenteVolumePublicoDefinido() =>
        Assert.Equal("/data/jwt-publica/public.pem", ExportadorChavePublica.ValidarCaminho("/data/jwt-publica/public.pem"));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("public.pem")]
    [InlineData("/data/jwt/private.pem")]
    [InlineData("/data/jwt/public.pem")]
    [InlineData("/data/jwt-publica/../jwt/private.pem")]
    public void RecusaDestinoForaDoVolumePublico(string caminho) =>
        Assert.Throws<InvalidOperationException>(() => ExportadorChavePublica.ValidarCaminho(caminho));

    [Fact]
    public void ExportaApenasComponentesPublicosEConservaArquivosPrivados()
    {
        using var rsa = RSA.Create(2048);
        var origem = Gravar("privado/public.pem", rsa.ExportSubjectPublicKeyInfoPem());
        var privada = Gravar("privado/private.pem", rsa.ExportPkcs8PrivateKeyPem());
        var privadaAntes = File.ReadAllBytes(privada);
        var destino = Caminho("publico/public.pem");

        ExportadorChavePublica.Exportar(origem, destino, CancellationToken.None);

        using var publica = RSA.Create();
        publica.ImportFromPem(File.ReadAllText(destino));
        Assert.Equal(rsa.ExportParameters(false).Modulus, publica.ExportParameters(false).Modulus);
        Assert.Equal(rsa.ExportParameters(false).Exponent, publica.ExportParameters(false).Exponent);
        Assert.Throws<CryptographicException>(() => publica.ExportParameters(true));
        Assert.Equal(privadaAntes, File.ReadAllBytes(privada));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(destino)!));
        if (OperatingSystem.IsLinux())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                File.GetUnixFileMode(destino));
            Assert.Equal((UnixFileMode)493, File.GetUnixFileMode(Path.GetDirectoryName(destino)!));
        }
    }

    [Fact]
    public void RecusaPemPrivadoSemAlterarPublicaAnterior()
    {
        using var rsa = RSA.Create(2048);
        var origem = Gravar("privado/public.pem", rsa.ExportPkcs8PrivateKeyPem());
        var destino = Gravar("publico/public.pem", "conteudo anterior");

        Assert.Throws<InvalidOperationException>(() => ExportadorChavePublica.Exportar(origem, destino, CancellationToken.None));
        Assert.Equal("conteudo anterior", File.ReadAllText(destino));
    }

    [Fact]
    public void RecusaBlocoAdicionalMesmoComPublicaValida()
    {
        using var rsa = RSA.Create(2048);
        var origem = Gravar("privado/public.pem", rsa.ExportSubjectPublicKeyInfoPem() + "\n" + rsa.ExportPkcs8PrivateKeyPem());

        Assert.Throws<InvalidOperationException>(() => ExportadorChavePublica.Exportar(origem, Caminho("publico/public.pem"), CancellationToken.None));
        Assert.False(Directory.Exists(Caminho("publico")));
    }

    [Fact]
    public void RecusaChaveRsaFraca()
    {
        using var rsa = RSA.Create(1024);
        var origem = Gravar("privado/public.pem", rsa.ExportSubjectPublicKeyInfoPem());
        Assert.Throws<InvalidOperationException>(() => ExportadorChavePublica.Exportar(origem, Caminho("publico/public.pem"), CancellationToken.None));
    }

    [Fact]
    public void RecusaDiretorioCompartilhadoComAOrigem()
    {
        using var rsa = RSA.Create(2048);
        var origem = Gravar("privado/public.pem", rsa.ExportSubjectPublicKeyInfoPem());
        Assert.Throws<InvalidOperationException>(() => ExportadorChavePublica.Exportar(origem, Caminho("privado/copia.pem"), CancellationToken.None));
    }

    [Fact]
    public void RepetirPreservaArquivoPublicoValidoSemReescrever()
    {
        using var rsa = RSA.Create(2048);
        var origem = Gravar("privado/public.pem", rsa.ExportSubjectPublicKeyInfoPem());
        var destino = Caminho("publico/public.pem");
        ExportadorChavePublica.Exportar(origem, destino, CancellationToken.None);
        var data = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(destino, data);

        ExportadorChavePublica.Exportar(origem, destino, CancellationToken.None);

        Assert.Equal(data, File.GetLastWriteTimeUtc(destino));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(destino)!));
    }

    [Fact]
    public void AtualizaCopiaPublicaSemDeixarTemporarios()
    {
        using var rsa = RSA.Create(2048);
        var origem = Gravar("privado/public.pem", rsa.ExportSubjectPublicKeyInfoPem());
        var destino = Gravar("publico/public.pem", "copia antiga");
        ExportadorChavePublica.Exportar(origem, destino, CancellationToken.None);
        Assert.Equal(rsa.ExportSubjectPublicKeyInfoPem() + "\n", File.ReadAllText(destino));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(destino)!));
    }

    [Fact]
    public void CancelamentoNaoPublicaArquivo()
    {
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            ExportadorChavePublica.Exportar(Caminho("privado/public.pem"), Caminho("publico/public.pem"), cancelamento.Token));
        Assert.False(Directory.Exists(Caminho("publico")));
    }

    public static bool ExecucaoLinux => OperatingSystem.IsLinux();

    [Fact(Skip = "Validação de links simbólicos do container Linux.", SkipUnless = nameof(ExecucaoLinux))]
    public void RecusaDestinoSimbolicoSemModificarArquivoApontado()
    {
        using var rsa = RSA.Create(2048);
        var origem = Gravar("privado/public.pem", rsa.ExportSubjectPublicKeyInfoPem());
        var privada = Gravar("privado/private.pem", rsa.ExportPkcs8PrivateKeyPem());
        var privadaAntes = File.ReadAllBytes(privada);
        var destino = Caminho("publico/public.pem");
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        File.CreateSymbolicLink(destino, privada);

        Assert.Throws<InvalidOperationException>(() => ExportadorChavePublica.Exportar(origem, destino, CancellationToken.None));
        Assert.Equal(privadaAntes, File.ReadAllBytes(privada));
    }

    [Fact(Skip = "Validação de links simbólicos do container Linux.", SkipUnless = nameof(ExecucaoLinux))]
    public void RecusaDiretorioSimbolicoSemExportarParaOPerimetroPrivado()
    {
        using var rsa = RSA.Create(2048);
        var origem = Gravar("privado/public.pem", rsa.ExportSubjectPublicKeyInfoPem());
        Directory.CreateSymbolicLink(Caminho("publico"), Caminho("privado"));

        Assert.Throws<InvalidOperationException>(() =>
            ExportadorChavePublica.Exportar(origem, Caminho("publico/copia.pem"), CancellationToken.None));
        Assert.False(File.Exists(Caminho("privado/copia.pem")));
    }
    private string Caminho(string relativo) => Path.Combine(_diretorio, relativo.Replace('/', Path.DirectorySeparatorChar));

    private string Gravar(string relativo, string conteudo)
    {
        var caminho = Caminho(relativo);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, conteudo);
        return caminho;
    }

    public void Dispose() => Directory.Delete(_diretorio, recursive: true);
}