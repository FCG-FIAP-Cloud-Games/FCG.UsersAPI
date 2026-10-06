using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using FCG.Users.Domain.Entities;
using FCG.Users.Infrastructure.Security;
using FCG.Users.IntegrationTests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace FCG.Users.IntegrationTests.Security;

public sealed class TestesServicosTokens
{
    [Theory]
    [InlineData(PerfisSistema.Usuario)]
    [InlineData(PerfisSistema.Administrador)]
    public void AccessTokenPossuiContratoMinimoEAssinaturaVerificavelSoComChavePublica(string perfil)
    {
        using var arquivos = new ChavesJwtTeste();
        var configuracao = CriarConfiguracao(arquivos.CriarConfiguracao());
        using var chaves = new ChavesJwtRsa(configuracao);
        var agora = new DateTimeOffset(2026, 10, 4, 18, 20, 30, TimeSpan.Zero).AddMilliseconds(987);
        var usuario = CriarUsuario();
        var servico = new ServicoTokenJwt(configuracao, chaves, new RelogioFixo(agora));
        var resposta = servico.GerarToken(usuario, perfil);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(resposta.AccessToken);
        Assert.Equal("Bearer", resposta.TokenType);
        Assert.Equal(900, resposta.ExpiresIn);
        Assert.Equal(SecurityAlgorithms.RsaSha256, token.Header.Alg);
        Assert.Equal(arquivos.KeyId, token.Header.Kid);
        Assert.Equal("FIAP.CloudGames", token.Issuer);
        Assert.Equal("FIAP.CloudGames.Api", Assert.Single(token.Audiences));
        Assert.Equal(usuario.Id.ToString(), token.Subject);
        Assert.Equal(perfil, Assert.Single(token.Claims, claim => claim.Type == "role").Value);
        Assert.True(Guid.TryParse(token.Id, out _));
        var esperado = DateTimeOffset.FromUnixTimeSeconds(agora.ToUnixTimeSeconds());
        Assert.Equal(esperado.UtcDateTime, token.IssuedAt);
        Assert.Equal(esperado.UtcDateTime, token.ValidFrom);
        Assert.Equal(esperado.AddMinutes(15), resposta.ExpiresAt);
        Assert.Equal(resposta.ExpiresAt.UtcDateTime, token.ValidTo);
        Assert.Equal(8, token.Claims.Count());
        Assert.DoesNotContain(token.Claims, claim => claim.Type is "name" or "email" or "cpf" or "senha" or "hash");
        Assert.DoesNotContain(usuario.Email, token.Payload.SerializeToJson(), StringComparison.Ordinal);
        Assert.DoesNotContain(usuario.Nome, token.Payload.SerializeToJson(), StringComparison.Ordinal);
        Assert.DoesNotContain(usuario.SenhaHash, token.Payload.SerializeToJson(), StringComparison.Ordinal);
        using var publica = RSA.Create();
        publica.ImportFromPem(File.ReadAllText(arquivos.Publica));
        var partes = resposta.AccessToken.Split('.');
        Assert.True(publica.VerifyData(Encoding.ASCII.GetBytes(partes[0] + "." + partes[1]),
            Base64UrlEncoder.DecodeBytes(partes[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        Assert.Throws<CryptographicException>(() => publica.ExportParameters(includePrivateParameters: true));
    }

    [Fact]
    public void EmissoesParaMesmoUsuarioPossuemIdentificadoresDistintos()
    {
        using var arquivos = new ChavesJwtTeste();
        var configuracao = CriarConfiguracao(arquivos.CriarConfiguracao());
        using var chaves = new ChavesJwtRsa(configuracao);
        var servico = new ServicoTokenJwt(configuracao, chaves, TimeProvider.System);
        var usuario = CriarUsuario();
        var primeiro = servico.GerarToken(usuario, PerfisSistema.Usuario);
        var segundo = servico.GerarToken(usuario, PerfisSistema.Usuario);
        var leitor = new JwtSecurityTokenHandler();
        Assert.NotEqual(primeiro.AccessToken, segundo.AccessToken);
        Assert.NotEqual(leitor.ReadJwtToken(primeiro.AccessToken).Id, leitor.ReadJwtToken(segundo.AccessToken).Id);
    }

    [Fact]
    public void UsuarioInativoNaoRecebeAccessToken()
    {
        using var arquivos = new ChavesJwtTeste();
        var configuracao = CriarConfiguracao(arquivos.CriarConfiguracao());
        using var chaves = new ChavesJwtRsa(configuracao);
        var usuario = CriarUsuario();
        usuario.Inativar(DateTimeOffset.UtcNow);
        var servico = new ServicoTokenJwt(configuracao, chaves, TimeProvider.System);
        Assert.Throws<InvalidOperationException>(() => servico.GerarToken(usuario, PerfisSistema.Usuario));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("usuario")]
    [InlineData("PerfilDesconhecido")]
    public void PerfilForaDoContratoNaoRecebeAccessToken(string perfil)
    {
        using var arquivos = new ChavesJwtTeste();
        var configuracao = CriarConfiguracao(arquivos.CriarConfiguracao());
        using var chaves = new ChavesJwtRsa(configuracao);
        var servico = new ServicoTokenJwt(configuracao, chaves, TimeProvider.System);
        Assert.Throws<InvalidOperationException>(() => servico.GerarToken(CriarUsuario(), perfil));
    }

    [Fact]
    public void ReinicioReutilizaArquivosEstaveisEContinuaValidandoTokensEmitidosAntes()
    {
        using var arquivos = new ChavesJwtTeste();
        var configuracao = CriarConfiguracao(arquivos.CriarConfiguracao());
        string tokenAntes;
        using (var primeiroProcesso = new ChavesJwtRsa(configuracao))
        {
            tokenAntes = new ServicoTokenJwt(configuracao, primeiroProcesso, TimeProvider.System)
                .GerarToken(CriarUsuario(), PerfisSistema.Usuario).AccessToken;
        }
        using var segundoProcesso = new ChavesJwtRsa(configuracao);
        var leitor = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = leitor.ValidateToken(tokenAntes, segundoProcesso.CriarParametrosValidacao(), out _);
        Assert.True(principal.Identity!.IsAuthenticated);
        Assert.True(principal.IsInRole(PerfisSistema.Usuario));
    }

    [Fact]
    public void RotacaoMudaAssinaturaEMantemTokensAntigosValidosEnquantoPublicaAntigaEstiverConfigurada()
    {
        using var antiga = new ChavesJwtTeste(keyId: "chave-antiga");
        using var nova = new ChavesJwtTeste(keyId: "chave-nova");
        var configuracaoAntiga = CriarConfiguracao(antiga.CriarConfiguracao());
        string tokenAntigo;
        using (var assinaturaAntiga = new ChavesJwtRsa(configuracaoAntiga))
        {
            tokenAntigo = new ServicoTokenJwt(configuracaoAntiga, assinaturaAntiga, TimeProvider.System)
                .GerarToken(CriarUsuario(), PerfisSistema.Usuario).AccessToken;
        }
        var dadosNovos = nova.CriarConfiguracao();
        dadosNovos["Jwt:Validation:Keys:1:KeyId"] = antiga.KeyId;
        dadosNovos["Jwt:Validation:Keys:1:PublicKeyPath"] = antiga.Publica;
        var configuracaoNova = CriarConfiguracao(dadosNovos);
        using var conjunto = new ChavesJwtRsa(configuracaoNova);
        var tokenNovo = new ServicoTokenJwt(configuracaoNova, conjunto, TimeProvider.System)
            .GerarToken(CriarUsuario(), PerfisSistema.Administrador).AccessToken;
        var leitor = new JwtSecurityTokenHandler { MapInboundClaims = false };
        Assert.True(leitor.ValidateToken(tokenAntigo, conjunto.CriarParametrosValidacao(), out _).IsInRole(PerfisSistema.Usuario));
        Assert.True(leitor.ValidateToken(tokenNovo, conjunto.CriarParametrosValidacao(), out _).IsInRole(PerfisSistema.Administrador));
        Assert.Equal(nova.KeyId, leitor.ReadJwtToken(tokenNovo).Header.Kid);
        using var somenteNova = new ChavesJwtRsa(CriarConfiguracao(nova.CriarConfiguracao()));
        Assert.ThrowsAny<SecurityTokenException>(() => leitor.ValidateToken(tokenAntigo, somenteNova.CriarParametrosValidacao(), out _));
    }

    [Fact]
    public void RefreshTokenEAleatorioOpacoBase64UrlEPersisteApenasHashDeterministico()
    {
        using var arquivos = new ChavesJwtTeste();
        var agora = new DateTimeOffset(2026, 10, 4, 18, 20, 30, TimeSpan.Zero);
        var servico = new ServicoRefreshToken(CriarConfiguracao(arquivos.CriarConfiguracao()), new RelogioFixo(agora));
        var primeiro = servico.GerarToken();
        var segundo = servico.GerarToken();
        Assert.NotEqual(primeiro.Valor, segundo.Valor);
        Assert.NotEqual(primeiro.Hash, segundo.Hash);
        Assert.Equal(86, primeiro.Valor.Length);
        Assert.All(primeiro.Valor, caractere => Assert.True(char.IsAsciiLetterOrDigit(caractere) || caractere is '-' or '_'));
        Assert.Equal(64, Base64UrlEncoder.DecodeBytes(primeiro.Valor).Length);
        Assert.Equal(64, primeiro.Hash.Length);
        Assert.Equal(servico.CalcularHash(primeiro.Valor), primeiro.Hash);
        Assert.NotEqual(primeiro.Valor, primeiro.Hash);
        Assert.Equal(agora, primeiro.CriadoEm);
        Assert.Equal(agora.AddDays(7), primeiro.ExpiraEm);
        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", servico.CalcularHash("abc"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void CalculoDoHashRecusaRefreshTokenVazio(string token)
    {
        using var arquivos = new ChavesJwtTeste();
        var servico = new ServicoRefreshToken(CriarConfiguracao(arquivos.CriarConfiguracao()), TimeProvider.System);
        Assert.Throws<ArgumentException>(() => servico.CalcularHash(token));
    }

    private static ConfiguracaoJwt CriarConfiguracao(Dictionary<string, string?> dados) =>
        ConfiguracaoJwt.Criar(new ConfigurationBuilder().AddInMemoryCollection(dados).Build());

    private static Usuario CriarUsuario() => new(
        Guid.NewGuid(), "Pessoa Ficticia E07", "12345678900", new DateOnly(2000, 5, 10),
        "pessoa.e07@example.test", "hash-ficticio", PerfisSistema.UsuarioId, DateTimeOffset.UtcNow);

    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }
}
