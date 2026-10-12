using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace FCG.Users.Infrastructure.Security;

public sealed class ChavesJwtRsa : IDisposable
{
    private readonly ConfiguracaoJwt _configuracao;
    private readonly Dictionary<string, RsaSecurityKey> _chavesPublicas = new(StringComparer.Ordinal);
    private readonly RSA _chavePrivada;
    private readonly SigningCredentials _credenciais;
    private bool _descartado;

    public ChavesJwtRsa(ConfiguracaoJwt configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        _configuracao = configuracao;
        _chavePrivada = CarregarChave(configuracao.PrivateKeyPath, privada: true);

        try
        {
            foreach (var chave in configuracao.ValidationKeys)
            {
                var rsa = CarregarChave(chave.PublicKeyPath, privada: false);
                _chavesPublicas.Add(chave.KeyId, new RsaSecurityKey(rsa)
                {
                    KeyId = chave.KeyId,
                    CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
                });
            }

            var parametrosPrivada = _chavePrivada.ExportParameters(includePrivateParameters: false);
            var parametrosPublica = _chavesPublicas[configuracao.ActiveKeyId].Rsa.ExportParameters(includePrivateParameters: false);
            if (!parametrosPrivada.Modulus!.AsSpan().SequenceEqual(parametrosPublica.Modulus)
                || !parametrosPrivada.Exponent!.AsSpan().SequenceEqual(parametrosPublica.Exponent))
            {
                throw new InvalidOperationException("A chave privada ativa não corresponde à chave pública de Jwt:Signing:ActiveKeyId.");
            }

            var chaveAssinatura = new RsaSecurityKey(_chavePrivada)
            {
                KeyId = configuracao.ActiveKeyId,
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
            };
            _credenciais = new SigningCredentials(chaveAssinatura, SecurityAlgorithms.RsaSha256);
        }
        catch
        {
            _chavePrivada.Dispose();
            foreach (var chave in _chavesPublicas.Values)
                chave.Rsa.Dispose();
            throw;
        }
    }

    public SigningCredentials CriarCredenciaisAssinatura()
    {
        ObjectDisposedException.ThrowIf(_descartado, this);
        return _credenciais;
    }

    public TokenValidationParameters CriarParametrosValidacao()
    {
        ObjectDisposedException.ThrowIf(_descartado, this);
        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateIssuer = true,
            ValidIssuer = _configuracao.Issuer,
            ValidateAudience = true,
            RequireAudience = true,
            ValidAudience = _configuracao.Audience,
            IgnoreTrailingSlashWhenValidatingAudience = false,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role",
            TryAllIssuerSigningKeys = false,
            IncludeTokenOnFailedValidation = false,
            LogTokenId = false,
            IssuerSigningKeyResolver = (_, _, kid, _) => ResolverChavePublica(kid)
        };
    }

    public void Dispose()
    {
        if (_descartado)
            return;

        _descartado = true;
        _chavePrivada.Dispose();
        foreach (var chave in _chavesPublicas.Values)
            chave.Rsa.Dispose();
    }

    private SecurityKey[] ResolverChavePublica(string? kid)
    {
        ObjectDisposedException.ThrowIf(_descartado, this);
        return kid is not null && _chavesPublicas.TryGetValue(kid, out var chave)
            ? [chave]
            : [];
    }

    private static RSA CarregarChave(string caminho, bool privada)
    {
        var rsa = RSA.Create();
        try
        {
            var pem = File.ReadAllText(caminho);
            if (!PemEncoding.TryFind(pem, out var campos))
                throw new CryptographicException();

            var label = pem[campos.Label];
            var labelCorreto = privada
                ? label is "PRIVATE KEY" or "RSA PRIVATE KEY"
                : label is "PUBLIC KEY" or "RSA PUBLIC KEY";
            var inicio = campos.Location.Start.GetOffset(pem.Length);
            var fim = campos.Location.End.GetOffset(pem.Length);
            if (!labelCorreto || !string.IsNullOrWhiteSpace(pem[..inicio]) || !string.IsNullOrWhiteSpace(pem[fim..]))
                throw new CryptographicException();

            rsa.ImportFromPem(pem);
            if (rsa.KeySize < 2048)
                throw new CryptographicException();

            return rsa;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException or NotSupportedException)
        {
            rsa.Dispose();
            // Não expor conteúdo, caminho real ou exceção original: o arquivo pode conter uma chave privada.
            var campo = privada ? "Jwt:Signing:PrivateKeyPath" : "Jwt:Validation:Keys:PublicKeyPath";
            var tipo = privada ? "privada não criptografada" : "pública, sem material privado";
            throw new InvalidOperationException($"Não foi possível carregar {campo}. Use um único bloco PEM RSA de chave {tipo}, com pelo menos 2048 bits, em arquivo legível.");
        }
    }
}
