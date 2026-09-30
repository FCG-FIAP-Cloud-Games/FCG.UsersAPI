using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FCG.Users.IntegrationTests.Support;

internal sealed class FabricaUsersApi : WebApplicationFactory<Program>
{
    private readonly string _ambiente;
    private readonly bool? _swaggerHabilitado;
    private readonly bool _incluirControllerTeste;

    public FabricaUsersApi(
        string ambiente = "Development",
        bool? swaggerHabilitado = null,
        bool incluirControllerTeste = false)
    {
        _ambiente = ambiente;
        _swaggerHabilitado = swaggerHabilitado;
        _incluirControllerTeste = incluirControllerTeste;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_ambiente);

        if (_swaggerHabilitado.HasValue)
        {
            builder.ConfigureAppConfiguration((_, configuracao) =>
                configuracao.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Swagger:Enabled"] = _swaggerHabilitado.Value ? "true" : "false"
                }));
        }

        if (_incluirControllerTeste)
        {
            builder.ConfigureTestServices(servicos =>
                servicos.AddControllers().AddApplicationPart(typeof(CenariosHttpController).Assembly));
        }
    }
}
