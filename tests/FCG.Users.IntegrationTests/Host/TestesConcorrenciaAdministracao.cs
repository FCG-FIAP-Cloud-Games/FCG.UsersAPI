using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FCG.Users.Api.Contracts.Auth;
using FCG.Users.Domain.Entities;
using FCG.Users.Infrastructure.Data;
using FCG.Users.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FCG.Users.IntegrationTests.Host;

[Collection(ColecaoBancoUsers.Nome)]
public sealed class TestesConcorrenciaAdministracao(BancoUsersFixture banco)
{
    private static CancellationToken Cancelamento => DadosE09.Cancelamento;

    [Theory]
    [InlineData("perfil", "refresh", true)]
    [InlineData("perfil", "refresh", false)]
    [InlineData("inativar", "refresh", true)]
    [InlineData("inativar", "refresh", false)]
    [InlineData("perfil", "login", true)]
    [InlineData("perfil", "login", false)]
    [InlineData("inativar", "login", true)]
    [InlineData("inativar", "login", false)]
    public async Task EmissaoEAuditoriaRespeitamOrdemDaAlteracaoAdministrativa(string alteracao, string emissao, bool administracaoPrimeiro)
    {
        var usuario = await DadosE09.GravarUsuarioAsync(banco);
        var nome = $"e09-disputa-{Guid.NewGuid():N}";
        var conexao = new NpgsqlConnectionStringBuilder(banco.Conexao) { ApplicationName = nome }.ConnectionString;
        var pausa = new PausaAdministracao(administracaoPrimeiro);
        await using var fabrica = new FabricaUsersApi(conexao: conexao, configurarServicos: servicos =>
            servicos.AddScoped(_ => new UsersDbContext(new DbContextOptionsBuilder<UsersDbContext>()
                .UseNpgsql(conexao).AddInterceptors(pausa).Options)));
        using var cliente = fabrica.CreateClient();
        // A barreira é habilitada somente depois de preparar a sessão inicial.
        var login = await DadosE09.LoginAsync(cliente, usuario);
        pausa.Habilitada = true;
        var admin = DadosE09.Jwt(fabrica, Guid.NewGuid(), true);
        Task<HttpResponseMessage> Administrar() => DadosE09.EnviarAsync(cliente,
            alteracao == "perfil" ? "PUT" : "DELETE", $"/api/v1/usuarios/{usuario.Id}" + (alteracao == "perfil" ? "/perfil" : ""),
            admin, alteracao == "perfil" ? new { perfilId = PerfisSistema.AdministradorId } : null);
        Task<HttpResponseMessage> Emitir() => emissao == "refresh"
            ? cliente.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.RefreshToken }, Cancelamento)
            : cliente.PostAsJsonAsync("/api/v1/auth/login", new { email = usuario.Email, senha = DadosE09.Senha }, Cancelamento);
        var primeira = administracaoPrimeiro ? Administrar() : Emitir();
        try
        {
            await pausa.Chegou.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancelamento);
            var segunda = administracaoPrimeiro ? Emitir() : Administrar();
            try { await EsperarBloqueioAsync(nome); }
            finally { pausa.Liberar.TrySetResult(); }
            using var resultadoPrimeiro = await primeira;
            using var resultadoSegundo = await segunda;
            var resultadoAdministracao = administracaoPrimeiro ? resultadoPrimeiro : resultadoSegundo;
            var resultadoEmissao = administracaoPrimeiro ? resultadoSegundo : resultadoPrimeiro;
            Assert.Equal(alteracao == "perfil" ? HttpStatusCode.OK : HttpStatusCode.NoContent, resultadoAdministracao.StatusCode);
            Assert.Equal(administracaoPrimeiro && alteracao == "inativar" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, resultadoEmissao.StatusCode);
            if (resultadoEmissao.IsSuccessStatusCode)
            {
                var par = (await resultadoEmissao.Content.ReadFromJsonAsync<RespostaLogin>(Cancelamento))!;
                Assert.Equal(administracaoPrimeiro && alteracao == "perfil" ? PerfisSistema.Administrador : PerfisSistema.Usuario, par.Usuario.Perfil);
                using var seguinte = await cliente.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = par.RefreshToken }, Cancelamento);
                Assert.Equal(alteracao == "inativar" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, seguinte.StatusCode);
                if (alteracao == "perfil")
                {
                    var renovado = (await seguinte.Content.ReadFromJsonAsync<RespostaLogin>(Cancelamento))!;
                    Assert.Equal(PerfisSistema.Administrador, renovado.Usuario.Perfil);
                }
            }
            await using var contexto = banco.CriarContexto();
            var atualizado = await contexto.Usuarios.SingleAsync(item => item.Id == usuario.Id, Cancelamento);
            Assert.Equal(alteracao != "inativar", atualizado.Ativo);
            Assert.Equal(alteracao == "perfil" ? PerfisSistema.AdministradorId : PerfisSistema.UsuarioId, atualizado.PerfilId);
            Assert.Equal(1, await contexto.LogsUsuarios.CountAsync(item => item.UsuarioId == usuario.Id, Cancelamento));
        }
        finally { pausa.Liberar.TrySetResult(); }
    }

    private async Task EsperarBloqueioAsync(string nome)
    {
        await using var conexao = new NpgsqlConnection(banco.Conexao);
        await conexao.OpenAsync(Cancelamento);
        await using var comando = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name=@nome AND wait_event_type='Lock' AND query LIKE '%FOR UPDATE%')", conexao);
        comando.Parameters.AddWithValue("nome", nome);
        var tempo = Stopwatch.StartNew();
        while (tempo.Elapsed < TimeSpan.FromSeconds(10))
        {
            if ((bool)(await comando.ExecuteScalarAsync(Cancelamento))!) return;
            await Task.Delay(20, Cancelamento);
        }
        Assert.Fail("Não foi observada a espera real pelo bloqueio de usuário.");
    }

    private sealed class PausaAdministracao(bool administracaoPrimeiro) : DbCommandInterceptor
    {
        private int _pausou;
        public bool Habilitada { get; set; }
        public TaskCompletionSource Chegou { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Liberar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private async Task PausarAsync(DbCommand comando, CancellationToken cancelamento)
        {
            if (!Habilitada) return;
            var sql = comando.CommandText;
            var devePausar = administracaoPrimeiro
                ? sql.Contains("UPDATE \"tb_Usuarios\"", StringComparison.Ordinal)
                : sql.Contains("UPDATE \"tb_Tokens\"", StringComparison.Ordinal) || sql.Contains("INSERT INTO \"tb_Tokens\"", StringComparison.Ordinal);
            if (devePausar && Interlocked.Exchange(ref _pausou, 1) == 0)
            {
                Chegou.TrySetResult();
                await Liberar.Task.WaitAsync(TimeSpan.FromSeconds(20), cancelamento);
            }
        }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        { await PausarAsync(command, cancellationToken); return result; }
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        { await PausarAsync(command, cancellationToken); return result; }
    }
}
