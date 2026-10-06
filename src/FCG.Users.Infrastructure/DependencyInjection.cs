using FCG.Users.Application.Abstractions.Repositories;
using FCG.Users.Application.Abstractions.Security;
using FCG.Users.Infrastructure.Security;
using FCG.Users.Infrastructure.Data;
using FCG.Users.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FCG.Users.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AdicionarSegurancaCadastros(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IHashSenha, HashSenhaPbkdf2>();
        services.AddScoped<IServicoHashSenha, ServicoHashSenha>();
        return services;
    }

    public static IServiceCollection AdicionarSegurancaAutenticacao(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(ConfiguracaoJwt.Criar(configuration));
        services.AddSingleton(provider => new ChavesJwtRsa(provider.GetRequiredService<ConfiguracaoJwt>()));
        services.AddScoped<IServicoTokenJwt, ServicoTokenJwt>();
        services.AddScoped<IServicoRefreshToken, ServicoRefreshToken>();
        return services;
    }

    public static IServiceCollection AdicionarPersistencia(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var conexao = ConfiguracaoBanco.ValidarConexao(
            configuration.GetConnectionString("UsersDatabase"));

        services.AddDbContext<UsersDbContext>(options => options.UseNpgsql(conexao));
        services.AddScoped<IRepositoryUsuarios, RepositorioUsuarios>();
        services.AddScoped<IRepositorioTokens, RepositorioTokens>();

        return services;
    }
}
