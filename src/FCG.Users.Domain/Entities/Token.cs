namespace FCG.Users.Domain.Entities;

public class Token
{
    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public string TokenHash { get; private set; }
    public DateTimeOffset DataCriacao { get; private set; }
    public DateTimeOffset DataExpiracao { get; private set; }
    public DateTimeOffset? DataRevogacao { get; private set; }

    private Token()
    {
        TokenHash = string.Empty;
    }

    public Token(
        Guid id,
        Guid usuarioId,
        string tokenHash,
        DateTimeOffset dataCriacao,
        DateTimeOffset dataExpiracao)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("O identificador do token não pode ser vazio.", nameof(id));

        if (usuarioId == Guid.Empty)
            throw new ArgumentException("O usuário do token é obrigatório.", nameof(usuarioId));

        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        if (dataCriacao.Offset != TimeSpan.Zero || dataExpiracao.Offset != TimeSpan.Zero)
            throw new ArgumentException("As datas do token devem estar em UTC.");

        if (dataExpiracao <= dataCriacao)
            throw new ArgumentException("A expiração deve ser posterior à criação.", nameof(dataExpiracao));

        Id = id;
        UsuarioId = usuarioId;
        TokenHash = tokenHash;
        DataCriacao = dataCriacao;
        DataExpiracao = dataExpiracao;
    }

    public bool EstaExpirado(DateTimeOffset agora) => DataExpiracao <= agora;

    public bool EstaRevogado() => DataRevogacao.HasValue;

    public bool EstaAtivo(DateTimeOffset agora) => !EstaRevogado() && !EstaExpirado(agora);

    public void Revogar(DateTimeOffset dataRevogacao)
    {
        if (dataRevogacao.Offset != TimeSpan.Zero)
            throw new ArgumentException("A data de revogação deve estar em UTC.", nameof(dataRevogacao));

        if (!DataRevogacao.HasValue)
            DataRevogacao = dataRevogacao;
    }
}
