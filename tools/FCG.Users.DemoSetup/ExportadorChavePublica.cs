using System.Security.Cryptography;
using System.Text;

namespace FCG.Users.DemoSetup;

/// <summary>Publica somente os componentes públicos RSA no volume dedicado ao Catalog.</summary>
internal static class ExportadorChavePublica
{
    internal const string CaminhoPermitido = "/data/jwt-publica/public.pem";
    private const UnixFileMode PermissoesPublica = UnixFileMode.UserRead | UnixFileMode.UserWrite
        | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    internal static string? ValidarCaminho(string? caminho)
    {
        if (caminho is not null && caminho != CaminhoPermitido)
            throw new InvalidOperationException("Jwt:PublicKeyExport:Path aceita somente o arquivo público definido para a demonstração.");
        return caminho;
    }

    internal static void Exportar(string origemPublica, string destinoPublica, CancellationToken tokenCancelamento)
    {
        tokenCancelamento.ThrowIfCancellationRequested();
        var origem = Path.GetFullPath(origemPublica);
        var destino = Path.GetFullPath(destinoPublica);
        var comparacao = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(Path.GetDirectoryName(origem), Path.GetDirectoryName(destino), comparacao))
            throw new InvalidOperationException("A exportação exige um diretório separado das chaves privadas.");

        RecusarLinks(origem);
        RecusarLinks(destino);
        // Nunca copia bytes da origem: aceita um único PEM público e reexporta apenas sua parte pública.
        var pem = File.ReadAllText(origem);
        if (!PemEncoding.TryFind(pem, out var campos)
            || pem[campos.Label] is not ("PUBLIC KEY" or "RSA PUBLIC KEY")
            || !string.IsNullOrWhiteSpace(pem[..campos.Location.Start.GetOffset(pem.Length)])
            || !string.IsNullOrWhiteSpace(pem[campos.Location.End.GetOffset(pem.Length)..]))
            throw new InvalidOperationException("A exportação exige um único bloco PEM público RSA, sem material privado.");

        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        if (rsa.KeySize < 2048)
            throw new InvalidOperationException("A exportação exige uma chave pública RSA de pelo menos 2048 bits.");
        var publica = rsa.ExportSubjectPublicKeyInfoPem() + "\n";
        var diretorio = Path.GetDirectoryName(destino)!;
        Directory.CreateDirectory(diretorio);
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(diretorio, PermissoesPublica | UnixFileMode.UserExecute
                | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

        if (File.Exists(destino) && File.ReadAllText(destino) == publica)
        {
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(destino, PermissoesPublica);
            return;
        }

        // Mesmo volume: rename substitui o arquivo de uma vez, sem expor um PEM parcial ao leitor.
        var temporario = Path.Combine(diretorio, $".publica-{Guid.NewGuid():N}.tmp");
        try
        {
            var opcoes = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (OperatingSystem.IsLinux())
                opcoes.UnixCreateMode = PermissoesPublica;
            using (var arquivo = new FileStream(temporario, opcoes))
            {
                var bytes = Encoding.UTF8.GetBytes(publica);
                arquivo.Write(bytes);
                arquivo.Flush(flushToDisk: true);
            }
            tokenCancelamento.ThrowIfCancellationRequested();
            RecusarLinks(destino);
            File.Move(temporario, destino, overwrite: true);
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(destino, PermissoesPublica);
        }
        finally
        {
            if (File.Exists(temporario))
                File.Delete(temporario);
        }
    }

    private static void RecusarLinks(string caminho)
    {
        for (string? atual = caminho; atual is not null; atual = Path.GetDirectoryName(atual))
        {
            FileSystemInfo item = Directory.Exists(atual) ? new DirectoryInfo(atual) : new FileInfo(atual);
            if (item.LinkTarget is not null || (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0))
                throw new InvalidOperationException("A exportação pública não aceita links simbólicos.");
        }
    }
}