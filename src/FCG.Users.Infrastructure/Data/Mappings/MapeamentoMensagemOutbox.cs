using FCG.Users.Domain.Entities;
using FCG.Users.Infrastructure.Data.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FCG.Users.Infrastructure.Data.Mappings;

public sealed class MapeamentoMensagemOutbox : IEntityTypeConfiguration<MensagemOutbox>
{
    public const string IndiceEventId = "IX_tb_Outbox_EventId";
    public const string IndicePendentes = "IX_tb_Outbox_Pendentes";

    public void Configure(EntityTypeBuilder<MensagemOutbox> builder)
    {
        builder.ToTable("tb_Outbox", table =>
        {
            table.HasCheckConstraint("CK_tb_Outbox_Ids", "\"EventId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"CorrelationId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_tb_Outbox_Attempts", "\"Attempts\" >= 0");
            table.HasCheckConstraint("CK_tb_Outbox_PayloadObjeto", "jsonb_typeof(\"Payload\") = 'object'");
        });
        builder.HasKey(mensagem => mensagem.Id);
        builder.Property(mensagem => mensagem.Id).ValueGeneratedNever();
        builder.Property(mensagem => mensagem.EventId).IsRequired();
        builder.Property(mensagem => mensagem.CorrelationId).IsRequired();
        builder.Property(mensagem => mensagem.UserId).IsRequired();
        builder.Property(mensagem => mensagem.EventType).HasMaxLength(255).IsRequired();
        builder.Property(mensagem => mensagem.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(mensagem => mensagem.OccurredAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(mensagem => mensagem.PublishedAt).HasColumnType("timestamp with time zone");
        builder.Property(mensagem => mensagem.Attempts).HasDefaultValue(0).IsRequired();
        builder.HasIndex(mensagem => mensagem.EventId).IsUnique().HasDatabaseName(IndiceEventId);
        builder.HasIndex(mensagem => mensagem.UserId);
        builder.HasIndex(mensagem => new { mensagem.OccurredAt, mensagem.Id })
            .HasFilter("\"PublishedAt\" IS NULL").HasDatabaseName(IndicePendentes);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(mensagem => mensagem.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
