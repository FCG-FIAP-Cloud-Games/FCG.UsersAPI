using FCG.Users.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FCG.Users.Infrastructure.Data.Mappings;

public sealed class MapeamentoUsuario : IEntityTypeConfiguration<Usuario>
{
    public const string IndiceEmail = "IX_tb_Usuarios_Email";
    public const string IndiceCpf = "IX_tb_Usuarios_CPF";

    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.ToTable("tb_Usuarios", table =>
        {
            table.HasCheckConstraint("CK_tb_Usuarios_CPF_Formato", "\"CPF\" ~ '^[0-9]{11}$'");
            table.HasCheckConstraint(
                "CK_tb_Usuarios_Email_Normalizado",
                "\"Email\" = lower(btrim(\"Email\"))");
        });

        builder.HasKey(usuario => usuario.Id);
        builder.Property(usuario => usuario.Id).ValueGeneratedNever();
        builder.Property(usuario => usuario.Nome).HasMaxLength(255).IsRequired();
        builder.Property(usuario => usuario.CPF).HasMaxLength(11).IsRequired();
        builder.Property(usuario => usuario.DataNascimento).HasColumnType("date").IsRequired();
        builder.Property(usuario => usuario.Email).HasMaxLength(255).IsRequired();
        builder.Property(usuario => usuario.SenhaHash).HasMaxLength(255).IsRequired();
        builder.Property(usuario => usuario.PerfilId).IsRequired();
        builder.Property(usuario => usuario.Ativo).IsRequired();
        builder.Property(usuario => usuario.CriadoEmUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(usuario => usuario.DataInativacao).HasColumnType("timestamp with time zone");

        builder.HasIndex(usuario => usuario.Email).IsUnique().HasDatabaseName(IndiceEmail);
        builder.HasIndex(usuario => usuario.CPF).IsUnique().HasDatabaseName(IndiceCpf);
        builder.HasIndex(usuario => usuario.PerfilId);
        builder.HasOne<Perfil>().WithMany().HasForeignKey(usuario => usuario.PerfilId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
