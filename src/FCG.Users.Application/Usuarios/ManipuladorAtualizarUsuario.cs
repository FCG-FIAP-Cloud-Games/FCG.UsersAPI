using FCG.Users.Application.Abstractions.Repositories;
using FCG.Users.Domain.Entities;

namespace FCG.Users.Application.Usuarios;

public sealed class ManipuladorAtualizarUsuario
{
    private const int TamanhoMinimoNome = 3;
    private readonly IRepositoryUsuarios _repositorioUsuarios;
    private readonly TimeProvider _relogio;

    public ManipuladorAtualizarUsuario(
        IRepositoryUsuarios repositorioUsuarios,
        TimeProvider relogio)
    {
        _repositorioUsuarios = repositorioUsuarios;
        _relogio = relogio;
    }

    public async Task<ResultadoAtualizarUsuario> ProcessarAsync(
        ComandoAtualizarUsuario comando,
        CancellationToken tokenCancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(comando);

        var nome = ManipuladorCriarUsuario.NormalizarNome(comando.Nome);
        var email = ManipuladorCriarUsuario.NormalizarEmail(comando.Email);
        var dataNascimento = comando.DataNascimento.ToUniversalTime();
        var erros = Validar(comando, nome, email, dataNascimento, _relogio.GetUtcNow());

        if (erros.Count > 0)
            return ResultadoAtualizarUsuario.DadosInvalidos(erros);

        var usuario = await _repositorioUsuarios.ObterPorIdAsync(comando.Id, tokenCancelamento);
        if (usuario is null)
            return ResultadoAtualizarUsuario.NaoEncontrado();

        if (await _repositorioUsuarios.ExisteEmailAsync(email!, comando.Id, tokenCancelamento))
            return ResultadoAtualizarUsuario.ConflitoEmail();

        usuario.AtualizarDados(nome, dataNascimento, email!);
        await _repositorioUsuarios.AtualizarAsync(usuario, tokenCancelamento);

        return ResultadoAtualizarUsuario.Atualizado(DadosUsuario.De(usuario));
    }

    private static Dictionary<string, string[]> Validar(
        ComandoAtualizarUsuario comando,
        string nome,
        string? email,
        DateTimeOffset dataNascimento,
        DateTimeOffset agora)
    {
        var erros = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (comando.Id == Guid.Empty)
            erros["id"] = ["Informe um identificador de usuário válido."];
        if (nome.Length is < TamanhoMinimoNome or > Usuario.TamanhoMaximoNome)
            erros["nome"] = [$"O nome deve conter entre {TamanhoMinimoNome} e {Usuario.TamanhoMaximoNome} caracteres."];
        if (email is null)
            erros["email"] = ["Informe um e-mail válido."];
        if (comando.DataNascimento == default || dataNascimento > agora)
            erros["dataNascimento"] = ["Informe uma data de nascimento válida e não futura."];
        return erros;
    }
}
