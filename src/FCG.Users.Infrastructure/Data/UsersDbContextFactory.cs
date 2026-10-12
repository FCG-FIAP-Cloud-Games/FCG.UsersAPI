using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FCG.Users.Infrastructure.Data;

/// <summary>Permite gerar/aplicar migrations sem iniciar a API ou depender de appsettings secretos.</summary>
public sealed class UsersDbContextFactory : IDesignTimeDbContextFactory<UsersDbContext>
{
    public UsersDbContext CreateDbContext(string[] args)
    {
        var conexao = ConfiguracaoBanco.ValidarConexao(
            Environment.GetEnvironmentVariable("ConnectionStrings__UsersDatabase"));

        var options = new DbContextOptionsBuilder<UsersDbContext>()
            .UseNpgsql(conexao)
            .Options;

        return new UsersDbContext(options);
    }
}
