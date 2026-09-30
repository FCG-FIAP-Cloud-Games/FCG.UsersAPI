using System;

namespace FCG.Users.Domain.Entities;

public sealed class Permissao
{
    public Guid Id { get; private set; }
    public string Nome { get; private set; }
    public string? Descricao { get; private set; }
    public Guid PerfilId { get; private set; }

    private Permissao()
    {
        Nome = string.Empty;
    }

    public Permissao(Guid id, string nome, string? descricao, Guid perfilId)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("O identificador da permissão não pode ser vazio.");

        if (string.IsNullOrWhiteSpace(nome))
            throw new ArgumentException("O nome da permissão é obrigatório.");

        if (perfilId == Guid.Empty)
            throw new ArgumentException("O identificador do perfil é obrigatório.");

        Id = id;
        Nome = nome;
        Descricao = descricao;
        PerfilId = perfilId;
    }
}
