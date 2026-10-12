using System.Security.Authentication;
using FCG.Notifications.Application.Messaging.Contracts;
using FCG.Users.Application.Messaging;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FCG.Users.Infrastructure.Messaging;

public static class ConfiguracaoMensageria
{
    public static IServiceCollection AdicionarPublicadorOutbox(this IServiceCollection servicos, IConfiguration configuracao)
    {
        servicos.AdicionarTransporteOutbox(configuracao);
        if (!ConfiguracaoPublicadorOutbox.Criar(configuracao).Habilitado) return servicos;
        Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton(servicos, TimeProvider.System);
        servicos.AddScoped<ProcessadorOutbox>();
        servicos.AddHostedService<PublicadorOutboxEmSegundoPlano>();
        return servicos;
    }
    public static IServiceCollection AdicionarTransporteOutbox(this IServiceCollection servicos, IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(servicos);
        var outbox = ConfiguracaoPublicadorOutbox.Criar(configuracao);
        servicos.AddSingleton(outbox);
        if (!outbox.Habilitado) return servicos;
        var rabbit = ConfiguracaoRabbitMq.Criar(configuracao);
        servicos.AddSingleton(rabbit);
        servicos.AddMassTransit(registro =>
        {
            registro.UsingRabbitMq((_, transporte) =>
            {
                transporte.Host(rabbit.Host, rabbit.Port, rabbit.VirtualHost, host =>
                {
                    host.Username(rabbit.Username);
                    host.Password(rabbit.Password);
                    host.PublisherConfirmation = true;
                    host.RequestedConnectionTimeout(outbox.TimeoutPublicacao);
                    host.ContinuationTimeout(outbox.TimeoutPublicacao);
                    if (rabbit.UseTls)
                        host.UseSsl(tls => { tls.Protocol = SslProtocols.Tls12 | SslProtocols.Tls13; tls.ServerName = rabbit.Host; });
                });
                transporte.Message<UserCreatedEvent>(mensagem =>
                    mensagem.SetEntityName(DefinicaoUserCreatedEvent.Tipo["urn:message:".Length..]));
            });
        });
        servicos.Configure<MassTransitHostOptions>(opcoes =>
        {
            // O host HTTP não deve esperar pelo broker. A conexão se recupera em segundo plano.
            opcoes.WaitUntilStarted = false;
            opcoes.StartTimeout = null;
            opcoes.StopTimeout = TimeSpan.FromSeconds(15);
        });
        servicos.AddSingleton<IPublicadorEventos, PublicadorRabbitMq>();
        return servicos;
    }
}
