using System.Globalization;
using FCG.Users.Application.Usuarios;
using FCG.Users.Domain.Entities;
using FCG.Users.Infrastructure.Repositories;
using Npgsql;

namespace FCG.Users.Api.Configuration;

public static class InicializacaoAdministradorLocal
{
    public static void ValidarAmbiente(IConfiguration configuracao, IHostEnvironment ambiente)
    {
        if (!ambiente.IsDevelopment())
            throw new InvalidOperationException("A inicialização local de administrador exige Development.");
        var conexao = new NpgsqlConnectionStringBuilder(configuracao.GetConnectionString("UsersDatabase"));
        if (conexao.Host is not ("127.0.0.1" or "localhost" or "::1"))
            throw new InvalidOperationException("A inicialização local de administrador exige banco em loopback.");
    }

    public static async Task<int> ExecutarAsync(IServiceProvider servicos, IConfiguration configuracao,
        CancellationToken tokenCancelamento = default)
    {
        await using var escopo = servicos.CreateAsyncScope();
        var inicializador = escopo.ServiceProvider.GetRequiredService<InicializadorAdministradorLocal>();
        DateOnly.TryParseExact(configuracao["AdministradorLocal:DataNascimento"], "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var nascimento);
        var comando = new ComandoCriarUsuario(configuracao["AdministradorLocal:Nome"] ?? string.Empty,
            configuracao["AdministradorLocal:CPF"] ?? string.Empty, nascimento,
            configuracao["AdministradorLocal:Email"] ?? string.Empty,
            configuracao["AdministradorLocal:Senha"] ?? string.Empty, PerfisSistema.AdministradorId);
        var resultado = await inicializador.ExecutarAsync(comando, tokenCancelamento);
        Console.WriteLine(resultado switch
        {
            StatusInicializacaoAdministrador.Criado => "Primeiro administrador local criado com auditoria.",
            StatusInicializacaoAdministrador.JaExistente => "Já existe administrador: nenhum usuário ou senha foi alterado.",
            StatusInicializacaoAdministrador.DadosInvalidos => "Configuração do administrador inválida; confira os campos sem imprimir credenciais.",
            _ => "Não foi possível criar o administrador: CPF/e-mail já cadastrado."
        });
        return resultado is StatusInicializacaoAdministrador.Criado or StatusInicializacaoAdministrador.JaExistente ? 0 : 1;
    }
}
