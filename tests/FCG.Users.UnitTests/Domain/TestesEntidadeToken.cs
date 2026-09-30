using FCG.Users.Domain.Entities;

namespace FCG.Users.UnitTests.Domain;

public sealed class TestesEntidadeToken
{
    private static readonly DateTimeOffset Agora =
        new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CriarComDadosValidosCriaTokenAtivo()
    {
        var token = CriarToken(Agora.AddDays(7));

        Assert.NotEqual(Guid.Empty, token.Id);
        Assert.NotEqual(Guid.Empty, token.UsuarioId);
        Assert.Equal("HASH_DO_REFRESH_TOKEN", token.TokenHash);
        Assert.False(token.EstaExpirado(Agora));
        Assert.False(token.EstaRevogado());
        Assert.True(token.EstaAtivo(Agora));
    }

    [Fact]
    public void CriarComIdVazioRejeitaToken()
    {
        Assert.Throws<ArgumentException>(() => new Token(
            Guid.Empty,
            Guid.NewGuid(),
            "HASH_DO_REFRESH_TOKEN",
            Agora,
            Agora.AddDays(7)));
    }

    [Fact]
    public void CriarComUsuarioIdVazioRejeitaToken()
    {
        Assert.Throws<ArgumentException>(() => new Token(
            Guid.NewGuid(),
            Guid.Empty,
            "HASH_DO_REFRESH_TOKEN",
            Agora,
            Agora.AddDays(7)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CriarComHashVazioRejeitaToken(string? tokenHash)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Token(
            Guid.NewGuid(),
            Guid.NewGuid(),
            tokenHash!,
            Agora,
            Agora.AddDays(7)));
    }

    [Fact]
    public void CriarComExpiracaoNaoPosteriorACriacaoRejeitaToken()
    {
        Assert.Throws<ArgumentException>(() => new Token(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "HASH_DO_REFRESH_TOKEN",
            Agora,
            Agora));
    }

    [Fact]
    public void EstaExpiradoQuandoExpiracaoFoiAtingidaRetornaVerdadeiro()
    {
        var token = CriarToken(Agora.AddMinutes(1));

        Assert.True(token.EstaExpirado(Agora.AddMinutes(1)));
        Assert.False(token.EstaAtivo(Agora.AddMinutes(1)));
    }

    [Fact]
    public void RevogarParaTokenAtivoRegistraDataEInativaToken()
    {
        var token = CriarToken(Agora.AddDays(7));
        var dataRevogacao = Agora.AddMinutes(5);

        token.Revogar(dataRevogacao);

        Assert.Equal(dataRevogacao, token.DataRevogacao);
        Assert.True(token.EstaRevogado());
        Assert.False(token.EstaAtivo(dataRevogacao));
    }

    [Fact]
    public void RevogarDuasVezesPreservaPrimeiraRevogacao()
    {
        var token = CriarToken(Agora.AddDays(7));
        var primeiraRevogacao = Agora.AddMinutes(5);

        token.Revogar(primeiraRevogacao);
        token.Revogar(Agora.AddMinutes(10));

        Assert.Equal(primeiraRevogacao, token.DataRevogacao);
    }

    private static Token CriarToken(DateTimeOffset dataExpiracao) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "HASH_DO_REFRESH_TOKEN",
            Agora,
            dataExpiracao);
}
