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
                var exchangeEvento = DefinicaoUserCreatedEvent.Tipo["urn:message:".Length..];
                transporte.Message<UserCreatedEvent>(mensagem => mensagem.SetEntityName(exchangeEvento));

                if (HabilitarTopologiaDemonstracao(configuracao))
                {
                    // Somente na demonstração, o publish declara a fila sem importar arquivos do broker.
                    // O vínculo direto ao exchange do evento não cria um consumidor no UsersAPI.
                    transporte.Publish<UserCreatedEvent>(publicacao =>
                        publicacao.BindQueue(exchangeEvento, "notifications-user-created", fila =>
                        {
                            fila.ExchangeType = "fanout";
                            fila.Durable = true;
                            fila.AutoDelete = false;
                            fila.Exclusive = false;
                        }));
                }
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

    private static bool HabilitarTopologiaDemonstracao(IConfiguration configuracao)
    {
        var ambienteAspNet = configuracao["ASPNETCORE_ENVIRONMENT"];
        // Não provisionar a assinatura da demonstração em Production ou com flags conflitantes.
        return OperatingSystem.IsLinux()
            && configuracao["DemonstracaoLocal:Habilitada"] == "true"
            && configuracao["DOTNET_ENVIRONMENT"] == "Development"
            && configuracao["DOTNET_RUNNING_IN_CONTAINER"] == "true"
            && (ambienteAspNet is null || ambienteAspNet == "Development");
    }
}
