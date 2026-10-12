using System.Diagnostics;

namespace FCG.Users.IntegrationTests.Support;

internal static class ArtefatoUsersApiE14
{
    private static readonly string[] Prefixos = ["Jwt__", "Outbox__", "RabbitMq__", "ConnectionStrings__", "AdministradorLocal__"];
    internal static async Task<string> PrepararAsync(CancellationToken cancelamento)
    {
        var fornecido = Environment.GetEnvironmentVariable("FCG_E14_PUBLISHED_API");
        if (!string.IsNullOrWhiteSpace(fornecido)) return Validar(fornecido);
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "FCG.Users.sln"))) raiz = raiz.Parent;
        if (raiz is null) throw new InvalidOperationException("A prova E14 exige fonte ou FCG_E14_PUBLISHED_API.");
        var saida = Path.Combine(raiz.FullName, "TestResults", "E14", "api-" + Guid.NewGuid().ToString("N"));
        var inicio = CriarInicio(raiz.FullName);
        foreach (var argumento in new[] { "publish", "src/FCG.Users.Api", "-c", "Release", "--no-restore", "--nologo", "-o", saida })
            inicio.ArgumentList.Add(argumento);
        var resultado = await ExecutarAsync(inicio, cancelamento);
        if (resultado.Codigo != 0) throw new InvalidOperationException("Não foi possível publicar o artefato E14. Execute dotnet publish para obter diagnósticos.");
        return Validar(saida);
    }

    internal static async Task<int> InicializarAdminAsync(string pasta, string conexao, string cpf,
        string email, string senha, CancellationToken cancelamento)
    {
        var inicio = CriarInicio(pasta);
        inicio.ArgumentList.Add(Path.Combine(pasta, "FCG.Users.Api.dll"));
        inicio.ArgumentList.Add("--inicializar-admin-local");
        foreach (var chave in inicio.Environment.Keys.Where(chave =>
            Prefixos
                .Any(prefixo => chave.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase))).ToArray())
            inicio.Environment.Remove(chave);
        inicio.Environment["DOTNET_ENVIRONMENT"] = "Development";
        inicio.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        inicio.Environment["ConnectionStrings__UsersDatabase"] = conexao;
        inicio.Environment["AdministradorLocal__Nome"] = "Administrador Sintético E14";
        inicio.Environment["AdministradorLocal__CPF"] = cpf;
        inicio.Environment["AdministradorLocal__DataNascimento"] = "2000-02-29";
        inicio.Environment["AdministradorLocal__Email"] = email;
        inicio.Environment["AdministradorLocal__Senha"] = senha;
        return (await ExecutarAsync(inicio, cancelamento)).Codigo;
    }

    private static string Validar(string pasta)
    {
        var absoluta = Path.GetFullPath(pasta);
        foreach (var nome in new[] { "FCG.Users.Api.dll", "FCG.Users.Api.deps.json", "FCG.Users.Api.runtimeconfig.json", "appsettings.json" })
            if (!File.Exists(Path.Combine(absoluta, nome))) throw new InvalidOperationException("O artefato publicado E14 está incompleto.");
        return absoluta;
    }

    private static ProcessStartInfo CriarInicio(string pasta) => new(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
    {
        WorkingDirectory = pasta, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
        RedirectStandardOutput = true, RedirectStandardError = true
    };

    private static async Task<(int Codigo, string Saida)> ExecutarAsync(ProcessStartInfo inicio, CancellationToken cancelamento)
    {
        using var processo = new Process { StartInfo = inicio };
        if (!processo.Start()) throw new InvalidOperationException("Não foi possível iniciar o processo E14.");
        var saida = processo.StandardOutput.ReadToEndAsync(cancelamento);
        var erro = processo.StandardError.ReadToEndAsync(cancelamento);
        try
        {
            await processo.WaitForExitAsync(cancelamento).WaitAsync(TimeSpan.FromSeconds(120), cancelamento);
            // A saída de CLI/publish fica em memória; nunca é copiada para uma evidência compartilhável.
            return (processo.ExitCode, await saida + await erro);
        }
        finally
        {
            if (!processo.HasExited)
            {
                processo.Kill(entireProcessTree: true);
                await processo.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}
