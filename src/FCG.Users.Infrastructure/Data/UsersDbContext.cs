using FCG.Users.Domain.Entities;
using FCG.Users.Infrastructure.Data.Mappings;
using Microsoft.EntityFrameworkCore;

namespace FCG.Users.Infrastructure.Data;

public sealed class UsersDbContext(DbContextOptions<UsersDbContext> options) : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Perfil> Perfis => Set<Perfil>();
    public DbSet<Token> Tokens => Set<Token>();
    public DbSet<LogUsuario> LogsUsuarios => Set<LogUsuario>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new MapeamentoPerfil());
        modelBuilder.ApplyConfiguration(new MapeamentoUsuario());
        modelBuilder.ApplyConfiguration(new MapeamentoToken());
        modelBuilder.ApplyConfiguration(new MapeamentoLogUsuario());
    }
}
