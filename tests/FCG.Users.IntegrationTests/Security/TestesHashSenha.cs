using FCG.Users.Application.Abstractions.Security;
using FCG.Users.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FCG.Users.IntegrationTests.Security;

public sealed class TestesHashSenha
{
    private const string Senha = "SenhaLegada@42";
    // Vetor independente produzido com hashlib.pbkdf2_hmac do Python: SHA256, 100000, salt 00..0f, 32 bytes.
    private const string HashLegado = "PBKDF2-SHA256$100000$AAECAwQFBgcICQoLDA0ODw==$tQC5P6xvYF+DbW+IGLwx8Z3mM3wyqmNXHxy/Ryc3bRk=";

    [Fact]
    public void CadastroGeraSaltAleatorioEHashVerificavelSemGuardarSenha()
    {
        using var servicos = CriarServicos();
        var criador = servicos.GetRequiredService<IHashSenha>();
        var verificador = servicos.GetRequiredService<IServicoHashSenha>();
        var primeiro = criador.Criar(Senha);
        var segundo = criador.Criar(Senha);
        Assert.NotEqual(primeiro, segundo);
        Assert.DoesNotContain(Senha, primeiro, StringComparison.Ordinal);
        Assert.InRange(primeiro.Length, 1, 255);
        Assert.True(verificador.Verificar(Senha, primeiro));
        Assert.True(verificador.Verificar(Senha, segundo));
        Assert.False(verificador.Verificar("SenhaErrada@42", primeiro));
        var partes = primeiro.Split('$');
        Assert.Equal("PBKDF2-SHA256", partes[0]);
        Assert.Equal("100000", partes[1]);
        Assert.Equal(16, Convert.FromBase64String(partes[2]).Length);
        Assert.Equal(32, Convert.FromBase64String(partes[3]).Length);
    }

    [Fact]
    public void VerificadorAceitaVetorIndependenteDoMonolitoERecusaSenhaDiferente()
    {
        using var servicos = CriarServicos();
        var verificador = servicos.GetRequiredService<IServicoHashSenha>();
        Assert.True(verificador.Verificar(Senha, HashLegado));
        Assert.False(verificador.Verificar("senhaLegada@42", HashLegado));
    }

    [Theory]
    [InlineData(PasswordHasherCompatibilityMode.IdentityV2)]
    [InlineData(PasswordHasherCompatibilityMode.IdentityV3)]
    public void VerificadorPreservaCompatibilidadeComHashIdentity(PasswordHasherCompatibilityMode modo)
    {
        var legado = new PasswordHasher<object>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = modo,
            IterationCount = 10000
        })).HashPassword(new object(), Senha);
        using var servicos = CriarServicos();
        var verificador = servicos.GetRequiredService<IServicoHashSenha>();
        Assert.True(verificador.Verificar(Senha, legado));
        Assert.False(verificador.Verificar("OutraSenha@42", legado));
        Assert.True(verificador.Verificar(Senha, verificador.GerarHash(Senha)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("hash-invalido!")]
    [InlineData("AQ==")]
    [InlineData("PBKDF2-SHA256$0$AA==$AA==")]
    [InlineData("PBKDF2-SHA256$-1$AA==$AA==")]
    [InlineData("PBKDF2-SHA256$1000001$AA==$AA==")]
    [InlineData("PBKDF2-SHA256$invalido$AA==$AA==")]
    [InlineData("PBKDF2-SHA256$100000$*$AA==")]
    [InlineData("PBKDF2-SHA256$100000$$AA==")]
    [InlineData("PBKDF2-SHA256$100000$AA==$")]
    [InlineData("PBKDF2-SHA256$100000$AA==$AA==$extra")]
    public void HashMalformadoOuCustoAbusivoNaoAutenticaNemLancaExcecao(string hash)
    {
        using var servicos = CriarServicos();
        Assert.False(servicos.GetRequiredService<IServicoHashSenha>().Verificar(Senha, hash));
    }

    [Fact]
    public void VerificadorRecusaHashAcimaDaCapacidadeDoBancoESenhaVazia()
    {
        using var servicos = CriarServicos();
        var verificador = servicos.GetRequiredService<IServicoHashSenha>();
        Assert.False(verificador.Verificar(Senha, new string('A', 256)));
        Assert.False(verificador.Verificar(string.Empty, HashLegado));
    }

    private static ServiceProvider CriarServicos() => new ServiceCollection()
        .AdicionarSegurancaCadastros().BuildServiceProvider();
}
