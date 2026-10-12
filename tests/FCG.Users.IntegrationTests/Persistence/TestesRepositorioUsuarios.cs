using FCG.Users.Application.Abstractions.Repositories;
using FCG.Users.Domain.Entities;
using FCG.Users.Infrastructure.Repositories;
using FCG.Users.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FCG.Users.IntegrationTests.Persistence;

[Collection(ColecaoBancoUsers.Nome)]
public sealed class TestesRepositorioUsuarios(BancoUsersFixture banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UsuarioPreservaDataCivilHashCompletoEInstantesUtcAoSerRecarregado()
    {
        var usuario = DadosPersistencia.NovoUsuario();
        await using (var escrita = banco.CriarContexto())
        {
            var repositorio = new RepositorioUsuarios(escrita);
            Assert.Equal(ResultadoGravacaoUsuario.Sucesso, await repositorio.TentarAdicionarAsync(usuario, LogUsuario.RegistrarCadastro(usuario.Id, usuario.CriadoEmUtc), Cancelamento));
        }
        await using var leitura = banco.CriarContexto();
        var carregado = await new RepositorioUsuarios(leitura).ObterPorIdAsync(usuario.Id, Cancelamento);
        Assert.NotNull(carregado);
        Assert.Equal(new DateOnly(2000, 2, 29), carregado.DataNascimento);
        Assert.Equal(usuario.SenhaHash, carregado.SenhaHash);
        Assert.Equal(255, carregado.SenhaHash.Length);
        Assert.Equal(usuario.CPF, carregado.CPF);
        Assert.Equal(usuario.CriadoEmUtc, carregado.CriadoEmUtc);
        Assert.Equal(TimeSpan.Zero, carregado.CriadoEmUtc.Offset);
        Assert.True(carregado.Ativo);
        Assert.Null(carregado.DataInativacao);
    }

    [Fact]
    public async Task ConsultasEncontramPerfilAtualEIgnoramProprioUsuarioNaValidacaoDeUnicidade()
    {
        var usuario = await DadosPersistencia.GravarUsuarioAsync(banco);
        await using var contexto = banco.CriarContexto();
        var repositorio = new RepositorioUsuarios(contexto);
        Assert.True(await repositorio.PerfilExisteAsync(PerfisSistema.UsuarioId, Cancelamento));
        Assert.False(await repositorio.PerfilExisteAsync(Guid.NewGuid(), Cancelamento));
        Assert.True(await repositorio.ExisteEmailAsync(usuario.Email, null, Cancelamento));
        Assert.True(await repositorio.ExisteCpfAsync(usuario.CPF, null, Cancelamento));
        Assert.False(await repositorio.ExisteEmailAsync(usuario.Email, usuario.Id, Cancelamento));
        Assert.False(await repositorio.ExisteCpfAsync(usuario.CPF, usuario.Id, Cancelamento));
        Assert.Equal(usuario.Id, (await repositorio.ObterPorEmailAsync(usuario.Email, Cancelamento))?.Id);
        var porEmail = await repositorio.ObterAutenticacaoPorEmailAsync(usuario.Email, Cancelamento);
        var porId = await repositorio.ObterAutenticacaoPorIdAsync(usuario.Id, Cancelamento);
        Assert.NotNull(porEmail);
        Assert.NotNull(porId);
        Assert.Equal(PerfisSistema.Usuario, porEmail.Perfil);
        Assert.Equal(usuario.Id, porId.Usuario.Id);
        Assert.Null(await repositorio.ObterAutenticacaoPorIdAsync(Guid.NewGuid(), Cancelamento));
        Assert.Null(await repositorio.ObterAutenticacaoPorEmailAsync($"{Guid.NewGuid():N}@exemplo.test", Cancelamento));

        var carregado = await repositorio.ObterPorIdAsync(usuario.Id, Cancelamento);
        Assert.NotNull(carregado);
        carregado.AlterarPerfil(PerfisSistema.AdministradorId);
        carregado.Inativar(DadosPersistencia.Agora);
        Assert.Equal(ResultadoGravacaoUsuario.Sucesso, await repositorio.AtualizarAsync(carregado, Cancelamento));
        var atualizado = await repositorio.ObterAutenticacaoPorIdAsync(usuario.Id, Cancelamento);
        Assert.NotNull(atualizado);
        Assert.Equal(PerfisSistema.Administrador, atualizado.Perfil);
        // O repositório devolve o estado; é o caso de uso de login que recusa o usuário inativo.
        Assert.False(atualizado.Usuario.Ativo);
        Assert.Equal(DadosPersistencia.Agora, atualizado.Usuario.DataInativacao);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CadastrosConcorrentesAposPreConsultaClassificamConflitoSemGravarDuplicidade(bool mesmoEmail)
    {
        var primeiro = DadosPersistencia.NovoUsuario();
        var segundo = DadosPersistencia.NovoUsuario(
            email: mesmoEmail ? primeiro.Email : null, cpf: mesmoEmail ? null : primeiro.CPF);
        await using var contextoA = banco.CriarContexto();
        await using var contextoB = banco.CriarContexto();
        var repositorioA = new RepositorioUsuarios(contextoA);
        var repositorioB = new RepositorioUsuarios(contextoB);
        // As duas solicitações viram o valor disponível; só a constraint decide a corrida.
        Assert.False(await repositorioA.ExisteEmailAsync(primeiro.Email, null, Cancelamento));
        Assert.False(await repositorioB.ExisteEmailAsync(segundo.Email, null, Cancelamento));
        Assert.False(await repositorioA.ExisteCpfAsync(primeiro.CPF, null, Cancelamento));
        Assert.False(await repositorioB.ExisteCpfAsync(segundo.CPF, null, Cancelamento));
        var resultados = await Task.WhenAll(
            repositorioA.TentarAdicionarAsync(primeiro, LogUsuario.RegistrarCadastro(primeiro.Id, primeiro.CriadoEmUtc), Cancelamento),
            repositorioB.TentarAdicionarAsync(segundo, LogUsuario.RegistrarCadastro(segundo.Id, segundo.CriadoEmUtc), Cancelamento));
        Assert.Single(resultados, resultado => resultado == ResultadoGravacaoUsuario.Sucesso);
        Assert.Single(resultados, resultado => resultado == (mesmoEmail
            ? ResultadoGravacaoUsuario.ConflitoEmail : ResultadoGravacaoUsuario.ConflitoCpf));
        await using var verificacao = banco.CriarContexto();
        Assert.Equal(1, await verificacao.Usuarios.CountAsync(
            usuario => usuario.Id == primeiro.Id || usuario.Id == segundo.Id, Cancelamento));
        // Após o conflito, o contexto perdedor não tenta salvar novamente a entidade rejeitada.
        var perdedor = resultados[0] != ResultadoGravacaoUsuario.Sucesso ? repositorioA : repositorioB;
        var novaTentativa = DadosPersistencia.NovoUsuario();
        Assert.Equal(ResultadoGravacaoUsuario.Sucesso,
            await perdedor.TentarAdicionarAsync(novaTentativa,
                LogUsuario.RegistrarCadastro(novaTentativa.Id, novaTentativa.CriadoEmUtc), Cancelamento));
        Assert.Equal(1, await verificacao.LogsUsuarios.CountAsync(
            log => log.UsuarioId == primeiro.Id || log.UsuarioId == segundo.Id, Cancelamento));
    }

    [Fact]
    public async Task AtualizacaoClassificaEmailOcupadoAposPreConsultaEPreservaDadosAnteriores()
    {
        var usuario = await DadosPersistencia.GravarUsuarioAsync(banco);
        var concorrente = DadosPersistencia.NovoUsuario();
        await using var contexto = banco.CriarContexto();
        var repositorio = new RepositorioUsuarios(contexto);
        var carregado = await repositorio.ObterPorIdAsync(usuario.Id, Cancelamento);
        Assert.NotNull(carregado);
        Assert.False(await repositorio.ExisteEmailAsync(concorrente.Email, usuario.Id, Cancelamento));
        await using (var outraSolicitacao = banco.CriarContexto())
            Assert.Equal(ResultadoGravacaoUsuario.Sucesso,
                await new RepositorioUsuarios(outraSolicitacao).TentarAdicionarAsync(concorrente, LogUsuario.RegistrarCadastro(concorrente.Id, concorrente.CriadoEmUtc), Cancelamento));
        carregado.AtualizarDados("Nome alterado", carregado.DataNascimento, concorrente.Email);
        Assert.Equal(ResultadoGravacaoUsuario.ConflitoEmail,
            await repositorio.AtualizarAsync(carregado, Cancelamento));
        await using var verificacao = banco.CriarContexto();
        var persistido = await verificacao.Usuarios.SingleAsync(item => item.Id == usuario.Id, Cancelamento);
        Assert.Equal(usuario.Email, persistido.Email);
        Assert.Equal(usuario.Nome, persistido.Nome);
    }

    [Fact]
    public async Task EdicaoDeDadosNaoDesfazInativacaoETrocaDePerfilConcorrentes()
    {
        var usuario = await DadosPersistencia.GravarUsuarioAsync(banco);
        await using var contextoEdicao = banco.CriarContexto();
        await using var contextoAdministracao = banco.CriarContexto();
        var edicao = new RepositorioUsuarios(contextoEdicao);
        var administracao = new RepositorioUsuarios(contextoAdministracao);
        var editar = await edicao.ObterPorIdAsync(usuario.Id, Cancelamento);
        var administrar = await administracao.ObterPorIdAsync(usuario.Id, Cancelamento);
        Assert.NotNull(editar);
        Assert.NotNull(administrar);
        administrar.Inativar(DadosPersistencia.Agora);
        administrar.AlterarPerfil(PerfisSistema.AdministradorId);
        Assert.Equal(ResultadoGravacaoUsuario.Sucesso,
            await administracao.AtualizarAsync(administrar, Cancelamento));
        editar.AtualizarDados("Nome atualizado", editar.DataNascimento, editar.Email);
        Assert.Equal(ResultadoGravacaoUsuario.Sucesso, await edicao.AtualizarAsync(editar, Cancelamento));
        await using var verificacao = banco.CriarContexto();
        var persistido = await verificacao.Usuarios.SingleAsync(item => item.Id == usuario.Id, Cancelamento);
        Assert.Equal("Nome atualizado", persistido.Nome);
        Assert.False(persistido.Ativo);
        Assert.Equal(PerfisSistema.AdministradorId, persistido.PerfilId);
        Assert.Equal(DadosPersistencia.Agora, persistido.DataInativacao);
    }

    [Fact]
    public async Task AtualizacaoRecusaEntidadeDesanexadaParaNaoSobrescreverCamposSemIntencao()
    {
        var usuario = await DadosPersistencia.GravarUsuarioAsync(banco);
        await using var contexto = banco.CriarContexto();
        var repositorio = new RepositorioUsuarios(contexto);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repositorio.AtualizarAsync(usuario, Cancelamento));
    }
}
