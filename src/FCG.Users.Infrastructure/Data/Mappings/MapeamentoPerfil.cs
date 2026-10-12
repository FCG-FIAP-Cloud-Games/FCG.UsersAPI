using FCG.Users.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FCG.Users.Infrastructure.Data.Mappings;

public sealed class MapeamentoPerfil : IEntityTypeConfiguration<Perfil>
{
    public void Configure(EntityTypeBuilder<Perfil> builder)
    {
        builder.ToTable("tb_Perfil");
        builder.HasKey(perfil => perfil.Id);
        builder.Property(perfil => perfil.Id).ValueGeneratedNever();
        builder.Property(perfil => perfil.Nome).HasMaxLength(255).IsRequired();
        builder.HasIndex(perfil => perfil.Nome).IsUnique().HasDatabaseName("IX_tb_Perfil_Nome");

        builder.HasData(
            new Perfil(PerfisSistema.UsuarioId, PerfisSistema.Usuario),
            new Perfil(PerfisSistema.AdministradorId, PerfisSistema.Administrador));
    }
}
