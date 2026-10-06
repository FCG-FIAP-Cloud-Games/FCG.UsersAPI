using FCG.Users.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FCG.Users.Infrastructure.Data.Mappings;

public sealed class MapeamentoToken : IEntityTypeConfiguration<Token>
{
    public void Configure(EntityTypeBuilder<Token> builder)
    {
        builder.ToTable("tb_Tokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedNever();
        builder.Property(token => token.UsuarioId).IsRequired();
        builder.Property(token => token.TokenHash).HasMaxLength(255).IsRequired();
        builder.Property(token => token.DataCriacao).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(token => token.DataExpiracao).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(token => token.DataRevogacao).HasColumnType("timestamp with time zone");

        builder.HasIndex(token => token.TokenHash).IsUnique().HasDatabaseName("IX_tb_Tokens_TokenHash");
        builder.HasIndex(token => token.UsuarioId);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(token => token.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
