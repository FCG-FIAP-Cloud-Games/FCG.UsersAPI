using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FCG.Users.IntegrationTests.Support;
using Microsoft.IdentityModel.Tokens;

namespace FCG.Users.IntegrationTests.Host;

public sealed class TestesJwksHttp
{
    private static readonly string[] CamposPublicos = ["alg", "e", "kid", "kty", "n", "use"];
    [Fact]
    public async Task EndpointPublicaSomenteComponentesPublicosDaChaveQueAssinaTokens()
    {
        await using var factory = new FabricaUsersApi();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/.well-known/jwks.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var key = Assert.Single(json.RootElement.GetProperty("keys").EnumerateArray());
        Assert.Equal(factory.Chaves.KeyId, key.GetProperty("kid").GetString());
        Assert.Equal("RSA", key.GetProperty("kty").GetString());
        Assert.Equal("RS256", key.GetProperty("alg").GetString());
        Assert.Equal(CamposPublicos, key.EnumerateObject().Select(p => p.Name).Order().ToArray());
        var token = JwtTeste.Criar(factory.Chaves);
        var rsa = new RsaSecurityKey(new RSAParameters
        {
            Modulus = Base64UrlEncoder.DecodeBytes(key.GetProperty("n").GetString()!),
            Exponent = Base64UrlEncoder.DecodeBytes(key.GetProperty("e").GetString()!)
        });
        new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = false, ValidateAudience = false, IssuerSigningKey = rsa,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ClockSkew = TimeSpan.Zero
        }, out _);
    }
}
