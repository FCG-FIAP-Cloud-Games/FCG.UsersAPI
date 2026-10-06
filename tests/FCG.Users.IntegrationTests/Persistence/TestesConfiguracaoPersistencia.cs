using FCG.Users.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FCG.Users.IntegrationTests.Persistence;

public sealed class TestesConfiguracaoPersistencia
{
    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("Host=localhost;Password=senha-marcadora-nao-expor;CampoInexistente=valor")]
    [InlineData("Database=users;Username=teste;Password=senha-marcadora-nao-expor")]
    [InlineData("Host=localhost;Username=teste;Password=senha-marcadora-nao-expor")]
    [InlineData("Host=localhost;Database=users;Password=senha-marcadora-nao-expor")]
    public void ConfiguracaoInvalidaFalhaComOrientacaoSemExporSegredo(string? conexao)
    {
        var configuracao = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:UsersDatabase"] = conexao }).Build();
        var servicos = new ServiceCollection();
        var erro = Assert.Throws<InvalidOperationException>(() => servicos.AdicionarPersistencia(configuracao));
        Assert.Contains("ConnectionStrings:UsersDatabase", erro.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("senha-marcadora-nao-expor", erro.ToString(), StringComparison.Ordinal);
        Assert.Null(erro.InnerException);
    }
}
