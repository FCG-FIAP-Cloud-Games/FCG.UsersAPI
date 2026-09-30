using FCG.Users.Application.Abstractions.Repositories;

namespace FCG.Users.Application.Usuarios;

public sealed class ManipuladorObterUsuario
{
    private readonly IRepositoryUsuarios _repositorioUsuarios;

    public ManipuladorObterUsuario(IRepositoryUsuarios repositorioUsuarios)
    {
        _repositorioUsuarios = repositorioUsuarios;
    }

    public async Task<ResultadoObterUsuario> ProcessarAsync(
        ConsultaObterUsuario consulta,
        CancellationToken tokenCancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);

        if (consulta.Id == Guid.Empty)
            return ResultadoObterUsuario.IdInvalido();

        var usuario = await _repositorioUsuarios.ObterPorIdAsync(
            consulta.Id,
            tokenCancelamento);

        return usuario is null
            ? ResultadoObterUsuario.NaoEncontrado()
            : ResultadoObterUsuario.Encontrado(DadosUsuario.De(usuario));
    }
}
