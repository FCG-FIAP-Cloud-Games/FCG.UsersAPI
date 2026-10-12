namespace FCG.Users.Domain.Entities;

public sealed class LogUsuario
{
    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public string Descricao { get; private set; }
    public DateTimeOffset DataCriacao { get; private set; }

    private LogUsuario()
    {
        Descricao = string.Empty;
    }

    private LogUsuario(Guid usuarioId, string descricao, DateTimeOffset dataCriacao)
    {
        if (usuarioId == Guid.Empty)
            throw new ArgumentException("O usuário do registro de auditoria é obrigatório.", nameof(usuarioId));

        if (dataCriacao.Offset != TimeSpan.Zero)
            throw new ArgumentException("A data de auditoria deve estar em UTC.", nameof(dataCriacao));

        Id = Guid.NewGuid();
        UsuarioId = usuarioId;
        Descricao = descricao;
        DataCriacao = dataCriacao;
    }

    // Descrições fixas evitam levar senhas, tokens ou dados pessoais para o histórico.
    public static LogUsuario RegistrarCadastro(Guid usuarioId, DateTimeOffset dataCriacao) =>
        new(usuarioId, "Usuário cadastrado.", dataCriacao);

    public static LogUsuario RegistrarAlteracaoDados(Guid usuarioId, DateTimeOffset dataCriacao) =>
        new(usuarioId, "Dados do usuário alterados.", dataCriacao);

    public static LogUsuario RegistrarTrocaPerfil(Guid usuarioId, DateTimeOffset dataCriacao) =>
        new(usuarioId, "Perfil do usuário alterado.", dataCriacao);

    public static LogUsuario RegistrarInativacao(Guid usuarioId, DateTimeOffset dataCriacao) =>
        new(usuarioId, "Usuário inativado.", dataCriacao);
}
