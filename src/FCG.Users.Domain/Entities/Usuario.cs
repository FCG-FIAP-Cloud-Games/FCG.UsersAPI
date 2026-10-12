namespace FCG.Users.Domain.Entities;

public sealed class Usuario
{
    public const int TamanhoMaximoNome = 100;
    public const int TamanhoMaximoCpf = 11;
    public const int TamanhoMaximoEmail = 150;

    public Guid Id { get; private set; }
    public string Nome { get; private set; }
    public string CPF { get; private set; }
    public DateOnly DataNascimento { get; private set; }
    public string Email { get; private set; }
    public string SenhaHash { get; private set; }
    public Guid PerfilId { get; private set; }
    public bool Ativo { get; private set; }
    public DateTimeOffset CriadoEmUtc { get; private set; }
    public DateTimeOffset? DataInativacao { get; private set; }

    private Usuario()
    {
        Nome = null!;
        CPF = null!;
        Email = null!;
        SenhaHash = null!;
    }

    public Usuario(
        Guid id,
        string nome,
        string cpf,
        DateOnly dataNascimento,
        string email,
        string senhaHash,
        Guid perfilId,
        DateTimeOffset criadoEmUtc)
    {
        ValidarIdentificador(id);
        ValidarDados(nome, dataNascimento, email, perfilId, criadoEmUtc);
        ArgumentException.ThrowIfNullOrWhiteSpace(cpf);
        ArgumentException.ThrowIfNullOrWhiteSpace(senhaHash);

        if (cpf.Length != TamanhoMaximoCpf || cpf.Any(caractere => caractere is < '0' or > '9'))
            throw new ArgumentException("O CPF deve conter exatamente 11 dígitos de 0 a 9, sem formatação.", nameof(cpf));

        Id = id;
        Nome = nome;
        CPF = cpf;
        DataNascimento = dataNascimento;
        Email = email;
        SenhaHash = senhaHash;
        PerfilId = perfilId;
        Ativo = true;
        CriadoEmUtc = criadoEmUtc;
    }

    public void AtualizarDados(
        string nome,
        DateOnly dataNascimento,
        string email)
    {
        ValidarDados(nome, dataNascimento, email, PerfilId, CriadoEmUtc);

        Nome = nome;
        DataNascimento = dataNascimento;
        Email = email;
    }

    public void AlterarPerfil(Guid perfilId)
    {
        if (perfilId == Guid.Empty)
            throw new ArgumentException("O perfil é obrigatório.", nameof(perfilId));

        PerfilId = perfilId;
    }

    public void Inativar(DateTimeOffset dataInativacao)
    {
        if (dataInativacao.Offset != TimeSpan.Zero)
            throw new ArgumentException("A data de inativação deve estar em UTC.", nameof(dataInativacao));

        Ativo = false;
        DataInativacao = dataInativacao;
    }

    private static void ValidarIdentificador(Guid id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("O identificador do usuário não pode ser vazio.", nameof(id));
    }

    private static void ValidarDados(
        string nome,
        DateOnly dataNascimento,
        string email,
        Guid perfilId,
        DateTimeOffset criadoEmUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nome);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        if (nome.Length > TamanhoMaximoNome)
            throw new ArgumentOutOfRangeException(nameof(nome));

        if (email.Length > TamanhoMaximoEmail)
            throw new ArgumentOutOfRangeException(nameof(email));

        if (perfilId == Guid.Empty)
            throw new ArgumentException("O perfil é obrigatório.", nameof(perfilId));

        if (dataNascimento == default)
            throw new ArgumentException("A data de nascimento é obrigatória.", nameof(dataNascimento));

        if (criadoEmUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("A data de criação deve estar em UTC.", nameof(criadoEmUtc));
    }
}
