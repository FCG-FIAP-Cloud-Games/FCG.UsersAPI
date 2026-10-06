using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FCG.Users.IntegrationTests.Support;

internal static class JwtTeste
{
    // Montagem independente da biblioteca que emite tokens em produção.
    public static string Criar(ChavesJwtTeste chaves, Action<Dictionary<string, object?>>? alterar = null,
        string? kid = null, string algoritmo = "RS256", bool omitirKid = false)
    {
        var agora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var cabecalho = new Dictionary<string, object?> { ["alg"] = algoritmo, ["typ"] = "JWT" };
        if (!omitirKid) cabecalho["kid"] = kid ?? chaves.KeyId;
        var corpo = new Dictionary<string, object?>
        {
            ["sub"] = Guid.NewGuid().ToString(), ["role"] = "Usuario",
            ["iss"] = "FIAP.CloudGames", ["aud"] = "FIAP.CloudGames.Api",
            ["iat"] = agora, ["nbf"] = agora - 1, ["exp"] = agora + 900,
            ["jti"] = Guid.NewGuid().ToString("N")
        };
        alterar?.Invoke(corpo);
        var dados = Base64Url(JsonSerializer.SerializeToUtf8Bytes(cabecalho)) + "." + Base64Url(JsonSerializer.SerializeToUtf8Bytes(corpo));
        var bytes = Encoding.ASCII.GetBytes(dados);
        var assinatura = algoritmo switch
        {
            "none" => [],
            "HS256" => HMACSHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(chaves.Publica)), bytes),
            "RS512" => chaves.Rsa.SignData(bytes, HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1),
            _ => chaves.Rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
        };
        return dados + "." + Base64Url(assinatura);
    }

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
