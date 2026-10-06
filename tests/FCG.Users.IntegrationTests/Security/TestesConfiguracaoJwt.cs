using System.Security.Cryptography;
using FCG.Users.Infrastructure.Security;
using FCG.Users.IntegrationTests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace FCG.Users.IntegrationTests.Security;

public sealed class TestesConfiguracaoJwt
{
    [Fact]
    public void ConfiguracaoPreservaContratoAprovadoECarregaSomenteRsaForte()
    {
        using var arquivos = new ChavesJwtTeste();
        var configuracao = CriarConfiguracao(arquivos.CriarConfiguracao());
        using var chaves = new ChavesJwtRsa(configuracao);
        Assert.Equal("FIAP.CloudGames", configuracao.Issuer);
        Assert.Equal("FIAP.CloudGames.Api", configuracao.Audience);
        Assert.Equal(15, configuracao.AccessTokenExpirationMinutes);
        Assert.Equal(7, configuracao.RefreshTokenExpirationDays);
        Assert.Equal(arquivos.KeyId, configuracao.ActiveKeyId);
        var assinatura = chaves.CriarCredenciaisAssinatura();
        Assert.Equal(SecurityAlgorithms.RsaSha256, assinatura.Algorithm);
        Assert.Equal(arquivos.KeyId, assinatura.Kid);
        var validacao = chaves.CriarParametrosValidacao();
        Assert.Equal(TimeSpan.FromSeconds(30), validacao.ClockSkew);
        Assert.Equal("sub", validacao.NameClaimType);
        Assert.Equal("role", validacao.RoleClaimType);
        var publica = Assert.IsType<RsaSecurityKey>(Assert.Single(validacao.IssuerSigningKeyResolver!("", null!, arquivos.KeyId, validacao)));
        Assert.Throws<CryptographicException>(() => publica.Rsa.ExportParameters(includePrivateParameters: true));
        Assert.Empty(validacao.IssuerSigningKeyResolver!("", null!, "desconhecida", validacao));
        Assert.Empty(validacao.IssuerSigningKeyResolver!("", null!, null!, validacao));
    }

    [Theory]
    [InlineData("Jwt:Issuer")]
    [InlineData("Jwt:Audience")]
    [InlineData("Jwt:AccessTokenExpirationMinutes")]
    [InlineData("Jwt:RefreshTokenExpirationDays")]
    [InlineData("Jwt:Signing:ActiveKeyId")]
    [InlineData("Jwt:Signing:PrivateKeyPath")]
    [InlineData("Jwt:Validation:Keys:0:KeyId")]
    [InlineData("Jwt:Validation:Keys:0:PublicKeyPath")]
    public void CampoObrigatorioAusenteFalhaSemExporValores(string campo)
    {
        using var arquivos = new ChavesJwtTeste();
        var dados = arquivos.CriarConfiguracao();
        dados.Remove(campo);
        var erro = Assert.Throws<InvalidOperationException>(() => CriarConfiguracao(dados));
        Assert.Contains(campo, erro.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(arquivos.Pasta, erro.ToString(), StringComparison.Ordinal);
        Assert.Null(erro.InnerException);
    }

    [Theory]
    [InlineData("Jwt:Issuer", " ")]
    [InlineData("Jwt:Audience", "FIAP.CloudGames.Api ")]
    [InlineData("Jwt:Signing:ActiveKeyId", "id/indevido")]
    [InlineData("Jwt:Validation:Keys:0:KeyId", "id com espaço")]
    [InlineData("Jwt:AccessTokenExpirationMinutes", "0")]
    [InlineData("Jwt:AccessTokenExpirationMinutes", "-1")]
    [InlineData("Jwt:AccessTokenExpirationMinutes", "1.5")]
    [InlineData("Jwt:AccessTokenExpirationMinutes", "2147483648")]
    [InlineData("Jwt:RefreshTokenExpirationDays", "0")]
    [InlineData("Jwt:RefreshTokenExpirationDays", "sete")]
    [InlineData("Jwt:RefreshTokenExpirationDays", "2147483647")]
    [InlineData("Jwt:Signing:PrivateKeyPath", "relativa/privada.pem")]
    [InlineData("Jwt:Validation:Keys:0:PublicKeyPath", "publica.pem")]
    public void ConfiguracaoInvalidaFalhaSemReproduzirConteudo(string campo, string valor)
    {
        using var arquivos = new ChavesJwtTeste();
        var dados = arquivos.CriarConfiguracao();
        dados[campo] = valor;
        var erro = Assert.Throws<InvalidOperationException>(() => CriarConfiguracao(dados));
        Assert.DoesNotContain(arquivos.Pasta, erro.ToString(), StringComparison.Ordinal);
        Assert.Null(erro.InnerException);
    }

    [Fact]
    public void SemChavesPublicasNaoInicializa()
    {
        using var arquivos = new ChavesJwtTeste();
        var dados = arquivos.CriarConfiguracao();
        dados.Remove("Jwt:Validation:Keys:0:KeyId");
        dados.Remove("Jwt:Validation:Keys:0:PublicKeyPath");
        Assert.Throws<InvalidOperationException>(() => CriarConfiguracao(dados));
    }

    [Fact]
    public void IdentificadoresDuplicadosNaoInicializam()
    {
        using var arquivos = new ChavesJwtTeste();
        var dados = arquivos.CriarConfiguracao();
        dados["Jwt:Validation:Keys:1:KeyId"] = arquivos.KeyId;
        dados["Jwt:Validation:Keys:1:PublicKeyPath"] = arquivos.Publica;
        Assert.Throws<InvalidOperationException>(() => CriarConfiguracao(dados));
    }

    [Fact]
    public void IdentificadorAtivoDeveEstarNoConjuntoDePublicas()
    {
        using var arquivos = new ChavesJwtTeste();
        var dados = arquivos.CriarConfiguracao();
        dados["Jwt:Signing:ActiveKeyId"] = "id-nao-configurado";
        Assert.Throws<InvalidOperationException>(() => CriarConfiguracao(dados));
    }

    [Fact]
    public void ChavePrivadaAtivaDeveCorresponderAPublicaComMesmoIdentificador()
    {
        using var arquivos = new ChavesJwtTeste();
        using var outra = new ChavesJwtTeste();
        var dados = arquivos.CriarConfiguracao();
        dados["Jwt:Validation:Keys:0:PublicKeyPath"] = outra.Publica;
        var erro = Assert.Throws<InvalidOperationException>(() => new ChavesJwtRsa(CriarConfiguracao(dados)));
        Assert.DoesNotContain(arquivos.Pasta, erro.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(outra.Pasta, erro.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ArquivoAusenteFalhaSemExporCaminhoOuExcecaoOriginal(bool privada)
    {
        using var arquivos = new ChavesJwtTeste();
        var dados = arquivos.CriarConfiguracao();
        var campo = privada ? "Jwt:Signing:PrivateKeyPath" : "Jwt:Validation:Keys:0:PublicKeyPath";
        dados[campo] = Path.Combine(arquivos.Pasta, "segredo-nao-encontrado.pem");
        var erro = Assert.Throws<InvalidOperationException>(() => new ChavesJwtRsa(CriarConfiguracao(dados)));
        Assert.DoesNotContain(arquivos.Pasta, erro.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("segredo-nao-encontrado", erro.ToString(), StringComparison.Ordinal);
        Assert.Null(erro.InnerException);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ArquivoMalformadoFalhaSemExporSeuConteudo(bool privada)
    {
        const string conteudoSecreto = "CONTEUDO-QUE-NAO-PODE-APARECER-EM-LOGS";
        using var arquivos = new ChavesJwtTeste();
        File.WriteAllText(privada ? arquivos.Privada : arquivos.Publica, conteudoSecreto);
        var erro = Assert.Throws<InvalidOperationException>(() => new ChavesJwtRsa(CriarConfiguracao(arquivos.CriarConfiguracao())));
        Assert.DoesNotContain(conteudoSecreto, erro.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(arquivos.Pasta, erro.ToString(), StringComparison.Ordinal);
        Assert.Null(erro.InnerException);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChaveRsaComMenosDe2048BitsNaoInicializa(bool privada)
    {
        using var arquivos = new ChavesJwtTeste();
        using var fraca = RSA.Create(1024);
        File.WriteAllText(privada ? arquivos.Privada : arquivos.Publica,
            privada ? fraca.ExportPkcs8PrivateKeyPem() : fraca.ExportSubjectPublicKeyInfoPem());
        Assert.Throws<InvalidOperationException>(() => new ChavesJwtRsa(CriarConfiguracao(arquivos.CriarConfiguracao())));
    }

    [Fact]
    public void MaterialPrivadoNoCaminhoDeValidacaoNuncaEAceito()
    {
        using var arquivos = new ChavesJwtTeste();
        var dados = arquivos.CriarConfiguracao();
        dados["Jwt:Validation:Keys:0:PublicKeyPath"] = arquivos.Privada;
        var erro = Assert.Throws<InvalidOperationException>(() => new ChavesJwtRsa(CriarConfiguracao(dados)));
        Assert.DoesNotContain(File.ReadAllText(arquivos.Privada), erro.ToString(), StringComparison.Ordinal);
        Assert.Null(erro.InnerException);
    }

    [Fact]
    public void MaterialPublicoNaoPodeSerUsadoParaAssinar()
    {
        using var arquivos = new ChavesJwtTeste();
        var dados = arquivos.CriarConfiguracao();
        dados["Jwt:Signing:PrivateKeyPath"] = arquivos.Publica;
        Assert.Throws<InvalidOperationException>(() => new ChavesJwtRsa(CriarConfiguracao(dados)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PemComMultiplosBlocosOuConteudoAdicionalERecusado(bool privada)
    {
        using var arquivos = new ChavesJwtTeste();
        var caminho = privada ? arquivos.Privada : arquivos.Publica;
        File.AppendAllText(caminho, Environment.NewLine + File.ReadAllText(caminho));
        Assert.Throws<InvalidOperationException>(() => new ChavesJwtRsa(CriarConfiguracao(arquivos.CriarConfiguracao())));
    }

    [Fact]
    public void FormatoRsaPkcs1TambemPodeSerCarregado()
    {
        using var arquivos = new ChavesJwtTeste();
        File.WriteAllText(arquivos.Privada, arquivos.Rsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(arquivos.Publica, arquivos.Rsa.ExportRSAPublicKeyPem());
        using var chaves = new ChavesJwtRsa(CriarConfiguracao(arquivos.CriarConfiguracao()));
        Assert.Equal(arquivos.KeyId, chaves.CriarCredenciaisAssinatura().Kid);
    }

    [Fact]
    public void DescarteDasChavesEIdempotenteEEvitaReutilizacao()
    {
        using var arquivos = new ChavesJwtTeste();
        var chaves = new ChavesJwtRsa(CriarConfiguracao(arquivos.CriarConfiguracao()));
        chaves.Dispose();
        chaves.Dispose();
        Assert.Throws<ObjectDisposedException>(chaves.CriarCredenciaisAssinatura);
        Assert.Throws<ObjectDisposedException>(chaves.CriarParametrosValidacao);
    }

    private static ConfiguracaoJwt CriarConfiguracao(Dictionary<string, string?> dados) =>
        ConfiguracaoJwt.Criar(new ConfigurationBuilder().AddInMemoryCollection(dados).Build());
}
