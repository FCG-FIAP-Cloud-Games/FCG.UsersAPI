using FCG.Users.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;

namespace FCG.Users.IntegrationTests.Configuration;

public sealed class TestesConfiguracaoOutbox
{
    [Fact]
    public void AusenciaDeHabilitacaoMantemPublicadorDesligado() =>
        Assert.False(ConfiguracaoPublicadorOutbox.Criar(Configurar()).Habilitado);

    [Fact]
    public void DesabilitadoNaoExigeRabbitOuOpcoesSemUso()
    {
        var configuracao = Configurar(("Outbox:Enabled", "false"), ("Outbox:BatchSize", "invalido"));
        var servicos = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        servicos.AdicionarTransporteOutbox(configuracao);
        Assert.DoesNotContain(servicos, item => item.ServiceType == typeof(MassTransit.IBus));
        Assert.DoesNotContain(servicos, item => item.ServiceType == typeof(ConfiguracaoRabbitMq));
    }

    [Fact]
    public void HabilitadoUsaLimitesPadraoEBackoffLimitado()
    {
        var opcoes = ConfiguracaoPublicadorOutbox.Criar(Configurar(("Outbox:Enabled", "true")));
        Assert.True(opcoes.Habilitado);
        Assert.Equal(20, opcoes.TamanhoLote);
        Assert.Equal(TimeSpan.FromSeconds(2), opcoes.Intervalo);
        Assert.Equal(TimeSpan.FromSeconds(10), opcoes.TimeoutPublicacao);
        Assert.Equal(TimeSpan.FromSeconds(5), opcoes.CalcularEspera(1));
        Assert.Equal(TimeSpan.FromSeconds(10), opcoes.CalcularEspera(2));
        Assert.Equal(TimeSpan.FromSeconds(20), opcoes.CalcularEspera(3));
        Assert.Equal(TimeSpan.FromSeconds(300), opcoes.CalcularEspera(int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => opcoes.CalcularEspera(0));
    }

    [Theory]
    [InlineData("Outbox:Enabled", "1")]
    [InlineData("Outbox:BatchSize", "0")]
    [InlineData("Outbox:BatchSize", "101")]
    [InlineData("Outbox:BatchSize", "1.5")]
    [InlineData("Outbox:PollIntervalSeconds", "0")]
    [InlineData("Outbox:PollIntervalSeconds", "0.09")]
    [InlineData("Outbox:PublishTimeoutSeconds", "61")]
    [InlineData("Outbox:PublishTimeoutSeconds", "NaN")]
    [InlineData("Outbox:InitialRetrySeconds", "-1")]
    [InlineData("Outbox:InitialRetrySeconds", "Infinity")]
    [InlineData("Outbox:MaxRetrySeconds", "86401")]
    [InlineData("Outbox:PollIntervalSeconds", "1,5")]
    public void ConfiguracaoInvalidaFalhaSemImprimirCredencial(string chave, string valor)
    {
        var configuracao = Configurar(("Outbox:Enabled", "true"));
        configuracao[chave] = valor;
        var erro = Assert.Throws<InvalidOperationException>(() => ConfiguracaoPublicadorOutbox.Criar(configuracao));
        Assert.Contains(chave, erro.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RetryMaximoMenorQueInicialERecusado() =>
        Assert.Throws<InvalidOperationException>(() => ConfiguracaoPublicadorOutbox.Criar(Configurar(
            ("Outbox:Enabled", "true"), ("Outbox:InitialRetrySeconds", "20"), ("Outbox:MaxRetrySeconds", "10"))));

    [Fact]
    public void RabbitPreservaSegredoEDistinguePortasTls()
    {
        var dados = Rabbit();
        var semTls = ConfiguracaoRabbitMq.Criar(Configurar(dados));
        Assert.Equal((ushort)5672, semTls.Port);
        Assert.Equal("senha-sintetica-privada", semTls.Password);
        Assert.DoesNotContain(semTls.Password, semTls.ToString(), StringComparison.Ordinal);
        var comTls = ConfiguracaoRabbitMq.Criar(Configurar([.. dados, ("RabbitMq:UseTls", "true")]));
        Assert.Equal((ushort)5671, comTls.Port);
        Assert.True(comTls.UseTls);
    }

    [Theory]
    [InlineData("RabbitMq:Host", "")]
    [InlineData("RabbitMq:Host", "amqp://usuario:senha@servidor")]
    [InlineData("RabbitMq:Host", "localhost:5672")]
    [InlineData("RabbitMq:Port", "0")]
    [InlineData("RabbitMq:Port", "65536")]
    [InlineData("RabbitMq:VirtualHost", "")]
    [InlineData("RabbitMq:Username", "")]
    [InlineData("RabbitMq:Password", "")]
    [InlineData("RabbitMq:UseTls", "invalido")]
    public void RabbitInvalidoERecusadoSemVazarCredenciais(string chave, string valor)
    {
        var configuracao = Configurar(Rabbit());
        configuracao[chave] = valor;
        var erro = Assert.Throws<InvalidOperationException>(() => ConfiguracaoRabbitMq.Criar(configuracao));
        Assert.Contains(chave, erro.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("senha-sintetica-privada", erro.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("usuario:senha", erro.ToString(), StringComparison.Ordinal);
    }

    private static (string Chave, string Valor)[] Rabbit() =>
    [
        ("RabbitMq:Host", "127.0.0.1"), ("RabbitMq:VirtualHost", "e12"),
        ("RabbitMq:Username", "produtor"), ("RabbitMq:Password", "senha-sintetica-privada")
    ];

    private static IConfigurationRoot Configurar(params (string Chave, string Valor)[] valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            valores.Select(item => new KeyValuePair<string, string?>(item.Chave, item.Valor))).Build();
}
