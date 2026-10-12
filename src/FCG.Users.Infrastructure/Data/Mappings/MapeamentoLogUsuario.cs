using FCG.Users.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FCG.Users.Infrastructure.Data.Mappings;

public sealed class MapeamentoLogUsuario : IEntityTypeConfiguration<LogUsuario>
{
    public void Configure(EntityTypeBuilder<LogUsuario> builder)
    {
        builder.ToTable("tb_LogUsuarios");
        builder.HasKey(log => log.Id);
        builder.Property(log => log.Id).ValueGeneratedNever();
        builder.Property(log => log.UsuarioId).IsRequired();
        builder.Property(log => log.Descricao).HasMaxLength(255).IsRequired();
        builder.Property(log => log.DataCriacao).HasColumnType("timestamp with time zone").IsRequired();

        builder.HasIndex(log => new { log.UsuarioId, log.DataCriacao });
        builder.HasOne<Usuario>().WithMany().HasForeignKey(log => log.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
