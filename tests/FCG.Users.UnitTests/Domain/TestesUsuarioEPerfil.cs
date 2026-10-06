using FCG.Users.Domain.Entities;

namespace FCG.Users.UnitTests.Domain;

public sealed class TestesUsuarioEPerfil
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CriarUsuarioComPerfilVazioRejeitaCadastro()
    {
        var erro = Assert.Throws<ArgumentException>(() => CriarUsuario(Guid.Empty));
        Assert.Equal("perfilId", erro.ParamName);
    }

    [Fact]
    public void CriarUsuarioPreservaPerfilInformadoEIniciaAtivo()
    {
        var usuario = CriarUsuario(PerfisSistema.UsuarioId);
        Assert.Equal(PerfisSistema.UsuarioId, usuario.PerfilId);
        Assert.True(usuario.Ativo);
        Assert.Null(usuario.DataInativacao);
    }

    [Fact]
    public void AtualizarComEmailAcimaDoLimiteNaoAlteraDadosAnteriores()
    {
        var usuario = CriarUsuario(PerfisSistema.UsuarioId);

        Assert.Throws<ArgumentOutOfRangeException>(() => usuario.AtualizarDados(
            "Nome alterado", DateOnly.FromDateTime(Agora.UtcDateTime).AddYears(-21), new string('a', Usuario.TamanhoMaximoEmail + 1)));

        Assert.Equal("Pessoa Exemplo", usuario.Nome);
        Assert.Equal("pessoa@exemplo.com", usuario.Email);
        Assert.Equal(DateOnly.FromDateTime(Agora.UtcDateTime).AddYears(-20), usuario.DataNascimento);
    }

    [Fact]
    public void AlterarParaPerfilVazioPreservaPerfilAnterior()
    {
        var usuario = CriarUsuario(PerfisSistema.UsuarioId);

        Assert.Throws<ArgumentException>(() => usuario.AlterarPerfil(Guid.Empty));

        Assert.Equal(PerfisSistema.UsuarioId, usuario.PerfilId);
    }

    [Fact]
    public void InativarPreservaIdentidadeERegistraData()
    {
        var usuario = CriarUsuario(PerfisSistema.UsuarioId);
        var idOriginal = usuario.Id;

        usuario.Inativar(Agora);

        Assert.False(usuario.Ativo);
        Assert.Equal(Agora, usuario.DataInativacao);
        Assert.Equal(idOriginal, usuario.Id);
    }

    [Fact]
    public void InativarComDataForaDeUtcNaoAlteraEstado()
    {
        var usuario = CriarUsuario(PerfisSistema.UsuarioId);

        Assert.Throws<ArgumentException>(() => usuario.Inativar(Agora.ToOffset(TimeSpan.FromHours(-3))));

        Assert.True(usuario.Ativo);
        Assert.Null(usuario.DataInativacao);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void CriarPerfilSemNomeRejeitaEntrada(string? nome)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Perfil(Guid.NewGuid(), nome!));
    }

    [Fact]
    public void CriarPerfilComIdentificadorVazioRejeitaEntrada()
    {
        Assert.Throws<ArgumentException>(() => new Perfil(Guid.Empty, "Administrador"));
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(0, -3)]
    public void CriarTokenComDatasForaDeUtcRejeitaEntrada(int fusoCriacao, int fusoExpiracao)
    {
        Assert.Throws<ArgumentException>(() => new Token(
            Guid.NewGuid(), Guid.NewGuid(), "HASH_SINTETICO",
            Agora.ToOffset(TimeSpan.FromHours(fusoCriacao)),
            Agora.AddDays(7).ToOffset(TimeSpan.FromHours(fusoExpiracao))));
    }

    [Theory]
    [InlineData("1234567890")]
    [InlineData("123456789012")]
    [InlineData("123.456.789-00")]
    [InlineData("１２３４５６７８９００")]
    [InlineData("١٢٣٤٥٦٧٨٩٠٠")]
    [InlineData("1234567890a")]
    public void CriarUsuarioExigeCpfNormalizadoComOnzeDigitosAscii(string cpf)
    {
        var erro = Assert.Throws<ArgumentException>(() => new Usuario(
            Guid.NewGuid(), "Pessoa Exemplo", cpf, new DateOnly(2000, 1, 1),
            "pessoa@exemplo.com", "hash-sintetico", PerfisSistema.UsuarioId, Agora));

        Assert.Equal("cpf", erro.ParamName);
    }

    [Fact]
    public void CriarUsuarioSemNascimentoRejeitaEntrada()
    {
        var erro = Assert.Throws<ArgumentException>(() => new Usuario(
            Guid.NewGuid(), "Pessoa Exemplo", "12345678900", default,
            "pessoa@exemplo.com", "hash-sintetico", PerfisSistema.UsuarioId, Agora));

        Assert.Equal("dataNascimento", erro.ParamName);
    }

    [Fact]
    public void AtualizarUsuarioSemNascimentoPreservaDadosAnteriores()
    {
        var usuario = CriarUsuario(PerfisSistema.UsuarioId);
        var nascimentoOriginal = usuario.DataNascimento;

        Assert.Throws<ArgumentException>(() => usuario.AtualizarDados("Novo Nome", default, "novo@exemplo.com"));

        Assert.Equal(nascimentoOriginal, usuario.DataNascimento);
        Assert.Equal("Pessoa Exemplo", usuario.Nome);
        Assert.Equal("pessoa@exemplo.com", usuario.Email);
    }

    private static Usuario CriarUsuario(Guid perfilId) => new(
        Guid.NewGuid(), "Pessoa Exemplo", "12345678900", DateOnly.FromDateTime(Agora.UtcDateTime).AddYears(-20),
        "pessoa@exemplo.com", "hash-sintetico", perfilId, Agora.AddDays(-1));
}
