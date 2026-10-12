using System.Globalization;
using FCG.Users.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FCG.Users.IntegrationTests.Support;

internal static class DadosPersistencia
{
    private static long _proximoCpf = 10000000000;

    public static DateTimeOffset Agora { get; } = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    public static Usuario NovoUsuario(string? email = null, string? cpf = null, Guid? perfilId = null) =>
        new(Guid.NewGuid(), "Usuário de teste", cpf ?? ProximoCpf(), new DateOnly(2000, 2, 29),
            email ?? $"{Guid.NewGuid():N}@exemplo.test", new string('h', 255),
            perfilId ?? PerfisSistema.UsuarioId, Agora);

    public static string ProximoCpf() =>
        Interlocked.Increment(ref _proximoCpf).ToString("D11", CultureInfo.InvariantCulture);

    public static Token NovoToken(Guid usuarioId, string? hash = null, DateTimeOffset? expiracao = null) =>
        new(Guid.NewGuid(), usuarioId, hash ?? Guid.NewGuid().ToString("N"),
            Agora.AddDays(-1), expiracao ?? Agora.AddDays(1));

    public static async Task<Usuario> GravarUsuarioAsync(BancoUsersFixture banco)
    {
        var usuario = NovoUsuario();
        await using var contexto = banco.CriarContexto();
        contexto.Set<Usuario>().Add(usuario);
        await contexto.SaveChangesAsync(TestContext.Current.CancellationToken);
        return usuario;
    }

    public static async Task GravarTokensAsync(BancoUsersFixture banco, params Token[] tokens)
    {
        await using var contexto = banco.CriarContexto();
        contexto.Set<Token>().AddRange(tokens);
        await contexto.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
