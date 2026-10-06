using FCG.Users.Application.Abstractions.Repositories;
using FCG.Users.Domain.Entities;
using FCG.Users.Infrastructure.Data;
using FCG.Users.Infrastructure.Data.Mappings;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FCG.Users.Infrastructure.Repositories;

public sealed class RepositorioUsuarios(UsersDbContext contexto) : IRepositoryUsuarios
{
    public Task<Usuario?> ObterPorIdAsync(Guid id, CancellationToken tokenCancelamento = default) =>
        contexto.Usuarios.SingleOrDefaultAsync(usuario => usuario.Id == id, tokenCancelamento);

    public Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken tokenCancelamento = default) =>
        contexto.Usuarios.AsNoTracking().SingleOrDefaultAsync(usuario => usuario.Email == email, tokenCancelamento);

    public Task<UsuarioAutenticacao?> ObterAutenticacaoPorEmailAsync(
        string email,
        CancellationToken tokenCancelamento = default) =>
        ConsultarAutenticacao(contexto.Usuarios.AsNoTracking().Where(usuario => usuario.Email == email))
            .SingleOrDefaultAsync(tokenCancelamento);

    public Task<UsuarioAutenticacao?> ObterAutenticacaoPorIdAsync(
        Guid id,
        CancellationToken tokenCancelamento = default) =>
        ConsultarAutenticacao(contexto.Usuarios.AsNoTracking().Where(usuario => usuario.Id == id))
            .SingleOrDefaultAsync(tokenCancelamento);

    public Task<bool> ExisteEmailAsync(
        string email,
        Guid? ignorarUsuarioId,
        CancellationToken tokenCancelamento = default) =>
        contexto.Usuarios.AsNoTracking().AnyAsync(
            usuario => usuario.Email == email && (!ignorarUsuarioId.HasValue || usuario.Id != ignorarUsuarioId.Value),
            tokenCancelamento);

    public Task<bool> ExisteCpfAsync(
        string cpf,
        Guid? ignorarUsuarioId,
        CancellationToken tokenCancelamento = default) =>
        contexto.Usuarios.AsNoTracking().AnyAsync(
            usuario => usuario.CPF == cpf && (!ignorarUsuarioId.HasValue || usuario.Id != ignorarUsuarioId.Value),
            tokenCancelamento);

    public Task<bool> PerfilExisteAsync(Guid perfilId, CancellationToken tokenCancelamento = default) =>
        contexto.Perfis.AsNoTracking().AnyAsync(perfil => perfil.Id == perfilId, tokenCancelamento);

    public Task<ResultadoGravacaoUsuario> TentarAdicionarAsync(
        Usuario usuario,
        LogUsuario registroCadastro,
        CancellationToken tokenCancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        ArgumentNullException.ThrowIfNull(registroCadastro);
        if (registroCadastro.UsuarioId != usuario.Id)
            throw new ArgumentException(
                "O registro de cadastro deve pertencer ao usuário cadastrado.", nameof(registroCadastro));

        contexto.Usuarios.Add(usuario);
        contexto.LogsUsuarios.Add(registroCadastro);
        // Um único SaveChanges confirma as duas inclusões na mesma transação do EF Core.
        return SalvarAsync(usuario, tokenCancelamento, registroCadastro);
    }

    public Task<ResultadoGravacaoUsuario> AtualizarAsync(
        Usuario usuario,
        CancellationToken tokenCancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        if (contexto.Entry(usuario).State == EntityState.Detached)
            throw new InvalidOperationException(
                "Carregue o usuário com ObterPorIdAsync no mesmo repositório/contexto antes de atualizá-lo.");

        // O rastreamento compara o estado original com o atual e grava somente os campos alterados.
        // Assim, editar nome/e-mail não sobrescreve uma inativação ou troca de perfil concorrente.
        return SalvarAsync(usuario, tokenCancelamento);
    }

    private IQueryable<UsuarioAutenticacao> ConsultarAutenticacao(IQueryable<Usuario> usuarios) =>
        from usuario in usuarios
        join perfil in contexto.Perfis.AsNoTracking() on usuario.PerfilId equals perfil.Id
        select new UsuarioAutenticacao(usuario, perfil.Nome);

    private async Task<ResultadoGravacaoUsuario> SalvarAsync(
        Usuario usuario,
        CancellationToken tokenCancelamento,
        LogUsuario? registroCadastro = null)
    {
        try
        {
            await contexto.SaveChangesAsync(tokenCancelamento);
            return ResultadoGravacaoUsuario.Sucesso;
        }
        catch (DbUpdateException exception)
        {
            if (registroCadastro is not null)
            {
                // O rollback desfaz o banco; desanexar evita repetir estas inclusões em outro SaveChanges.
                contexto.Entry(registroCadastro).State = EntityState.Detached;
                contexto.Entry(usuario).State = EntityState.Detached;
            }

            if (exception.InnerException is PostgresException
                {
                    SqlState: PostgresErrorCodes.UniqueViolation,
                    ConstraintName: MapeamentoUsuario.IndiceEmail or MapeamentoUsuario.IndiceCpf
                } conflito)
            {
                contexto.Entry(usuario).State = EntityState.Detached;
                return conflito.ConstraintName == MapeamentoUsuario.IndiceEmail
                    ? ResultadoGravacaoUsuario.ConflitoEmail
                    : ResultadoGravacaoUsuario.ConflitoCpf;
            }

            throw;
        }
    }
}
