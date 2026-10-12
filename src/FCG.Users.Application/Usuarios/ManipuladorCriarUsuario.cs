using System.Net.Mail;
using FCG.Users.Application.Abstractions.Repositories;
using FCG.Users.Application.Abstractions.Security;
using FCG.Users.Domain.Entities;

namespace FCG.Users.Application.Usuarios;

public sealed class ManipuladorCriarUsuario
{
    private const int TamanhoMinimoNome = 3;
    private const int TamanhoMinimoSenha = 8;
    private readonly IRepositoryUsuarios _repositorioUsuarios;
    private readonly IHashSenha _hashSenha;
    private readonly TimeProvider _relogio;

    public ManipuladorCriarUsuario(
        IRepositoryUsuarios repositorioUsuarios,
        IHashSenha hashSenha,
        TimeProvider relogio)
    {
        _repositorioUsuarios = repositorioUsuarios;
        _hashSenha = hashSenha;
        _relogio = relogio;
    }

    public async Task<ResultadoCriarUsuario> ProcessarAsync(
        ComandoCriarUsuario comando,
        CancellationToken tokenCancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(comando);

        var nome = NormalizarNome(comando.Nome);
        var cpf = NormalizarCpf(comando.CPF);
        var email = NormalizarEmail(comando.Email);
        var dataNascimento = comando.DataNascimento;
        var erros = Validar(comando, nome, cpf, email, dataNascimento, _relogio.GetUtcNow());

        if (erros.Count > 0)
            return ResultadoCriarUsuario.DadosInvalidos(erros);

        if (!await _repositorioUsuarios.PerfilExisteAsync(comando.PerfilId, tokenCancelamento))
            return ResultadoCriarUsuario.PerfilNaoEncontrado();

        if (await _repositorioUsuarios.ExisteEmailAsync(email!, null, tokenCancelamento))
            return ResultadoCriarUsuario.ConflitoEmail();

        if (await _repositorioUsuarios.ExisteCpfAsync(cpf, null, tokenCancelamento))
            return ResultadoCriarUsuario.ConflitoCpf();

        var agora = _relogio.GetUtcNow();
        var usuario = new Usuario(
            Guid.NewGuid(),
            nome,
            cpf,
            dataNascimento,
            email!,
            _hashSenha.Criar(comando.Senha),
            comando.PerfilId,
            agora);
        var registroCadastro = LogUsuario.RegistrarCadastro(usuario.Id, agora);

        var gravacao = await _repositorioUsuarios.TentarAdicionarAsync(usuario, registroCadastro, tokenCancelamento);

        return gravacao switch
        {
            ResultadoGravacaoUsuario.Sucesso => ResultadoCriarUsuario.Criado(DadosUsuario.De(usuario)),
            ResultadoGravacaoUsuario.ConflitoEmail => ResultadoCriarUsuario.ConflitoEmail(),
            ResultadoGravacaoUsuario.ConflitoCpf => ResultadoCriarUsuario.ConflitoCpf(),
            _ => throw new InvalidOperationException("O repositório retornou um resultado de cadastro desconhecido.")
        };
    }

    internal static string NormalizarNome(string? nome) =>
        string.Join(' ', (nome ?? string.Empty).Split(
            ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    internal static string NormalizarCpf(string? cpf) =>
        new((cpf ?? string.Empty).Where(caractere => caractere is not '.' and not '-' && !char.IsWhiteSpace(caractere)).ToArray());

    internal static string? NormalizarEmail(string? email)
    {
        var valor = email?.Trim();
        if (string.IsNullOrWhiteSpace(valor)
            || valor.Length > Usuario.TamanhoMaximoEmail
            || !MailAddress.TryCreate(valor, out var endereco)
            || !string.Equals(endereco.Address, valor, StringComparison.OrdinalIgnoreCase))
            return null;

        return endereco.Address.ToLowerInvariant();
    }

    private static Dictionary<string, string[]> Validar(
        ComandoCriarUsuario comando,
        string nome,
        string cpf,
        string? email,
        DateOnly dataNascimento,
        DateTimeOffset agora)
    {
        var erros = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (nome.Length is < TamanhoMinimoNome or > Usuario.TamanhoMaximoNome)
            erros["nome"] = [$"O nome deve conter entre {TamanhoMinimoNome} e {Usuario.TamanhoMaximoNome} caracteres."];
        if (cpf.Length != Usuario.TamanhoMaximoCpf || cpf.Any(caractere => caractere is < '0' or > '9'))
            erros["cpf"] = ["O CPF deve conter exatamente 11 dígitos de 0 a 9."];
        if (email is null)
            erros["email"] = ["Informe um e-mail válido."];
        if (comando.DataNascimento == default || dataNascimento > DateOnly.FromDateTime(agora.UtcDateTime))
            erros["dataNascimento"] = ["Informe uma data de nascimento válida e não futura."];
        if (!SenhaValida(comando.Senha))
            erros["senha"] = ["A senha deve ter ao menos 8 caracteres, com maiúscula, minúscula, número e caractere especial."];
        if (comando.PerfilId == Guid.Empty)
            erros["perfilId"] = ["Informe um perfil válido."];

        return erros;
    }

    private static bool SenhaValida(string? senha) =>
        !string.IsNullOrWhiteSpace(senha)
        && senha.Length >= TamanhoMinimoSenha
        && senha.Any(char.IsUpper)
        && senha.Any(char.IsLower)
        && senha.Any(char.IsDigit)
        && senha.Any(caractere => !char.IsLetterOrDigit(caractere));
}
