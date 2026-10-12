using FCG.Users.Application.Abstractions.Repositories;
using FCG.Users.Domain.Entities;

namespace FCG.Users.Application.Usuarios;

public sealed class ManipuladorAtualizarUsuario
{
    private const int TamanhoMinimoNome = 3;
    private readonly IRepositoryUsuarios _repositorioUsuarios;
    private readonly TimeProvider _relogio;
    private readonly IUnidadeDeTrabalhoUsuarios _unidadeDeTrabalho;

    public ManipuladorAtualizarUsuario(
        IRepositoryUsuarios repositorioUsuarios,
        TimeProvider relogio,
        IUnidadeDeTrabalhoUsuarios unidadeDeTrabalho)
    {
        _repositorioUsuarios = repositorioUsuarios;
        _relogio = relogio;
        _unidadeDeTrabalho = unidadeDeTrabalho;
    }

    public async Task<ResultadoAtualizarUsuario> ProcessarAsync(
        ComandoAtualizarUsuario comando,
        CancellationToken tokenCancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(comando);

        var nome = ManipuladorCriarUsuario.NormalizarNome(comando.Nome);
        var email = ManipuladorCriarUsuario.NormalizarEmail(comando.Email);
        var dataNascimento = comando.DataNascimento;
        var erros = Validar(comando, nome, email, dataNascimento, _relogio.GetUtcNow());

        if (erros.Count > 0)
            return ResultadoAtualizarUsuario.DadosInvalidos(erros);

        return await _unidadeDeTrabalho.ExecutarAsync(comando.Id, async cancelamento =>
        {
            var usuario = await _repositorioUsuarios.ObterPorIdAsync(comando.Id, cancelamento);
            if (usuario is null)
                return ResultadoAtualizarUsuario.NaoEncontrado();

            if (await _repositorioUsuarios.ExisteEmailAsync(email!, comando.Id, cancelamento))
                return ResultadoAtualizarUsuario.ConflitoEmail();

            if (usuario.Nome == nome && usuario.DataNascimento == dataNascimento && usuario.Email == email)
                return ResultadoAtualizarUsuario.Atualizado(DadosUsuario.De(usuario));
            usuario.AtualizarDados(nome, dataNascimento, email!);
            var gravacao = await _repositorioUsuarios.AtualizarAsync(usuario, LogUsuario.RegistrarAlteracaoDados(usuario.Id, _relogio.GetUtcNow()), cancelamento);

            return gravacao switch
            {
                ResultadoGravacaoUsuario.Sucesso => ResultadoAtualizarUsuario.Atualizado(DadosUsuario.De(usuario)),
                ResultadoGravacaoUsuario.ConflitoEmail => ResultadoAtualizarUsuario.ConflitoEmail(),
                _ => throw new InvalidOperationException("O repositório retornou um conflito inesperado ao atualizar dados sem alterar o CPF.")
            };
        }, tokenCancelamento);
    }

    private static Dictionary<string, string[]> Validar(
        ComandoAtualizarUsuario comando,
        string nome,
        string? email,
        DateOnly dataNascimento,
        DateTimeOffset agora)
    {
        var erros = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (comando.Id == Guid.Empty)
            erros["id"] = ["Informe um identificador de usuário válido."];
        if (nome.Length is < TamanhoMinimoNome or > Usuario.TamanhoMaximoNome)
            erros["nome"] = [$"O nome deve conter entre {TamanhoMinimoNome} e {Usuario.TamanhoMaximoNome} caracteres."];
        if (email is null)
            erros["email"] = ["Informe um e-mail válido."];
        if (comando.DataNascimento == default || dataNascimento > DateOnly.FromDateTime(agora.UtcDateTime))
            erros["dataNascimento"] = ["Informe uma data de nascimento válida e não futura."];
        return erros;
    }
}
