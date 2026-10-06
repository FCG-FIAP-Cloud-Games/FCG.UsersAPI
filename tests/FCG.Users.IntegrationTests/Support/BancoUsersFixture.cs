using FCG.Users.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace FCG.Users.IntegrationTests.Support;

[CollectionDefinition(Nome)]
public sealed class ColecaoBancoUsers : ICollectionFixture<BancoUsersFixture>
{
    public const string Nome = "PostgreSQL UsersAPI";
}

public sealed class BancoUsersFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("users_integracao")
        .WithUsername("users_teste")
        .WithPassword(Guid.NewGuid().ToString("N"))
        .Build();

    internal string Conexao => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        // Porta aleatória e container descartável: nenhum banco do desenvolvedor é utilizado.
        await _container.StartAsync(TestContext.Current.CancellationToken);
        await using var contexto = CriarContexto();
        // Exercita a migration real, incluindo constraints e dados iniciais.
        await contexto.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public UsersDbContext CriarContexto()
    {
        var opcoes = new DbContextOptionsBuilder<UsersDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new UsersDbContext(opcoes);
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
