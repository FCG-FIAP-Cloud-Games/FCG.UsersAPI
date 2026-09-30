namespace FCG.Users.Domain.Entities;

public class Perfil
{
    public Guid Id { get; private set; }
    public string Nome { get; private set; }

    protected Perfil()
    {
        Nome = string.Empty;
    }

    public Perfil(Guid id, string nome)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("O identificador do perfil não pode ser vazio.", nameof(id));

        ArgumentException.ThrowIfNullOrWhiteSpace(nome);

        Id = id;
        Nome = nome;
    }

    public Perfil(string nome)
        : this(Guid.NewGuid(), nome)
    {
    }
}
