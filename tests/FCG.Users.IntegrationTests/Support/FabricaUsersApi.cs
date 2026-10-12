using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FCG.Users.IntegrationTests.Support;

internal sealed class FabricaUsersApi : WebApplicationFactory<Program>
{
    private readonly string _ambiente;
    private readonly bool _possuiChaves;
    private readonly IReadOnlyDictionary<string, string?>? _sobrescreverConfiguracao;
    internal ChavesJwtTeste Chaves { get; }
    private readonly string _conexao;
    private readonly Action<IServiceCollection>? _configurarServicos;
    private readonly bool? _swaggerHabilitado;
    private readonly bool _incluirControllerTeste;

    public FabricaUsersApi(
        string ambiente = "Development",
        bool? swaggerHabilitado = null,
        bool incluirControllerTeste = false,
        string? conexao = null,
        Action<IServiceCollection>? configurarServicos = null,
        ChavesJwtTeste? chaves = null,
        IReadOnlyDictionary<string, string?>? sobrescreverConfiguracao = null)
    {
        _ambiente = ambiente;
        _possuiChaves = chaves is null;
        Chaves = chaves ?? new ChavesJwtTeste();
        _sobrescreverConfiguracao = sobrescreverConfiguracao;
        _conexao = conexao ?? "Host=127.0.0.1;Port=1;Database=users_http_test;Username=teste;Password=teste";
        _configurarServicos = configurarServicos;
        _swaggerHabilitado = swaggerHabilitado;
        _incluirControllerTeste = incluirControllerTeste;
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // No host minimal, a conexão é validada antes de Build. Esta configuração precisa
        // chegar antes do entry point; ConfigureAppConfiguration do web host chega depois.
        builder.ConfigureHostConfiguration(configuracao =>
        {
            var valores = Chaves.CriarConfiguracao();
            valores["ConnectionStrings:UsersDatabase"] = _conexao;
            if (_sobrescreverConfiguracao is not null)
                foreach (var valor in _sobrescreverConfiguracao) valores[valor.Key] = valor.Value;
            configuracao.AddInMemoryCollection(valores);
        });
        return base.CreateHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            base.Dispose(disposing);
        }
        finally
        {
            if (disposing && _possuiChaves) Chaves.Dispose();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            if (_possuiChaves) Chaves.Dispose();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_ambiente);

        builder.ConfigureAppConfiguration((_, configuracao) =>
        {
            var valores = new Dictionary<string, string?>();

            if (_swaggerHabilitado.HasValue)
                valores["Swagger:Enabled"] = _swaggerHabilitado.Value ? "true" : "false";

            configuracao.AddInMemoryCollection(valores);
        });

        if (_configurarServicos is not null)
            builder.ConfigureTestServices(_configurarServicos);

        if (_incluirControllerTeste)
        {
            builder.ConfigureTestServices(servicos =>
                servicos.AddControllers().AddApplicationPart(typeof(CenariosHttpController).Assembly));
        }
    }
}
