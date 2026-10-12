using System.Security.Cryptography;

namespace FCG.Users.IntegrationTests.Support;

// Chaves descartáveis de teste: nunca utiliza chaves ou bancos do desenvolvedor.
internal sealed class ChavesJwtTeste : IDisposable
{
    private bool _descartada;
    public ChavesJwtTeste(int tamanho = 2048, string keyId = "teste-e07")
    {
        KeyId = keyId;
        Pasta = Path.Combine(Path.GetTempPath(), $"fcg-usersapi-e07-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Pasta);
        Privada = Path.Combine(Pasta, "privada.pem");
        Publica = Path.Combine(Pasta, "publica.pem");
        Rsa = RSA.Create(tamanho);
        File.WriteAllText(Privada, Rsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(Publica, Rsa.ExportSubjectPublicKeyInfoPem());
    }

    public string KeyId { get; }
    public string Pasta { get; }
    public string Privada { get; }
    public string Publica { get; }
    public RSA Rsa { get; }
    public Dictionary<string, string?> CriarConfiguracao() => new(StringComparer.Ordinal)
    {
        ["Jwt:Issuer"] = "FIAP.CloudGames",
        ["Jwt:Audience"] = "FIAP.CloudGames.Api",
        ["Jwt:AccessTokenExpirationMinutes"] = "15",
        ["Jwt:RefreshTokenExpirationDays"] = "7",
        ["Jwt:Signing:ActiveKeyId"] = KeyId,
        ["Jwt:Signing:PrivateKeyPath"] = Privada,
        ["Jwt:Validation:Keys:0:KeyId"] = KeyId,
        ["Jwt:Validation:Keys:0:PublicKeyPath"] = Publica
    };

    public void Dispose()
    {
        if (_descartada) return;
        _descartada = true;
        Rsa.Dispose();
        // Exclui somente os dois arquivos criados por esta instância, nunca de forma recursiva.
        File.Delete(Privada);
        File.Delete(Publica);
        Directory.Delete(Pasta, recursive: false);
    }
}
