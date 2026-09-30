namespace FCG.Users.Domain.Entities;

public sealed class Usuario
{
    public const int TamanhoMaximoNome = 100;
    public const int TamanhoMaximoCpf = 100;
    public const int TamanhoMaximoEmail = 150;

    public Guid Id { get; private set; }
    public string Nome { get; private set; }
    public string CPF { get; private set; }
    public DateTimeOffset DataNascimento { get; private set; }
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
        DateTimeOffset dataNascimento,
        string email,
        string senhaHash,
        Guid perfilId,
        DateTimeOffset criadoEmUtc)
    {
        ValidarIdentificador(id);
        ValidarDados(nome, dataNascimento, email, perfilId, criadoEmUtc);
        ArgumentException.ThrowIfNullOrWhiteSpace(cpf);
        ArgumentException.ThrowIfNullOrWhiteSpace(senhaHash);

        if (cpf.Length > TamanhoMaximoCpf)
            throw new ArgumentOutOfRangeException(nameof(cpf));

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
        DateTimeOffset dataNascimento,
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
        DateTimeOffset dataNascimento,
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

        if (dataNascimento.Offset != TimeSpan.Zero)
            throw new ArgumentException("A data de nascimento deve estar em UTC.", nameof(dataNascimento));

        if (criadoEmUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("A data de criação deve estar em UTC.", nameof(criadoEmUtc));
    }
}
