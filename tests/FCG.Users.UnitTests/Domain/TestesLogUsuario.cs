using FCG.Users.Domain.Entities;

namespace FCG.Users.UnitTests.Domain;

public sealed class TestesLogUsuario
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RegistrarAcoesMantemUsuarioEInstanteComIdentificadoresDistintos()
    {
        var usuarioId = Guid.NewGuid();
        LogUsuario[] registros =
        [
            LogUsuario.RegistrarCadastro(usuarioId, Agora),
            LogUsuario.RegistrarAlteracaoDados(usuarioId, Agora),
            LogUsuario.RegistrarTrocaPerfil(usuarioId, Agora),
            LogUsuario.RegistrarInativacao(usuarioId, Agora)
        ];

        Assert.All(registros, registro =>
        {
            Assert.NotEqual(Guid.Empty, registro.Id);
            Assert.Equal(usuarioId, registro.UsuarioId);
            Assert.Equal(Agora, registro.DataCriacao);
        });
        Assert.Equal(4, registros.Select(registro => registro.Id).Distinct().Count());
        Assert.Equal(4, registros.Select(registro => registro.Descricao).Distinct().Count());
        Assert.All(registros, registro => Assert.InRange(registro.Descricao.Length, 1, 255));
    }

    [Fact]
    public void RegistrarAcaoSemUsuarioRejeitaHistoricoSemResponsavel()
    {
        Assert.Throws<ArgumentException>(() => LogUsuario.RegistrarCadastro(Guid.Empty, Agora));
        Assert.Throws<ArgumentException>(() => LogUsuario.RegistrarAlteracaoDados(Guid.Empty, Agora));
        Assert.Throws<ArgumentException>(() => LogUsuario.RegistrarTrocaPerfil(Guid.Empty, Agora));
        Assert.Throws<ArgumentException>(() => LogUsuario.RegistrarInativacao(Guid.Empty, Agora));
    }

    [Fact]
    public void RegistrarAcaoComFusoDiferenteDeUtcRejeitaData()
    {
        var usuarioId = Guid.NewGuid();
        var dataLocal = Agora.ToOffset(TimeSpan.FromHours(-3));

        Assert.Throws<ArgumentException>(() => LogUsuario.RegistrarCadastro(usuarioId, dataLocal));
        Assert.Throws<ArgumentException>(() => LogUsuario.RegistrarAlteracaoDados(usuarioId, dataLocal));
        Assert.Throws<ArgumentException>(() => LogUsuario.RegistrarTrocaPerfil(usuarioId, dataLocal));
        Assert.Throws<ArgumentException>(() => LogUsuario.RegistrarInativacao(usuarioId, dataLocal));
    }
}
