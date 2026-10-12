using System.Security.Cryptography;
using FCG.Users.Application.Abstractions.Security;
using Microsoft.AspNetCore.Identity;

namespace FCG.Users.Infrastructure.Security;

public sealed class ServicoHashSenha : IServicoHashSenha
{
    private const string PrefixoPbkdf2Sha256 = "PBKDF2-SHA256";
    private const int MaximoIteracoesAceitas = 1_000_000;
    private static readonly object UsuarioHash = new();
    private readonly PasswordHasher<object> _passwordHasher = new();

    public string GerarHash(string senha)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(senha);
        return _passwordHasher.HashPassword(UsuarioHash, senha);
    }

    public bool Verificar(string senha, string senhaHash)
    {
        if (string.IsNullOrWhiteSpace(senha) || string.IsNullOrWhiteSpace(senhaHash) || senhaHash.Length > 255)
            return false;

        if (senhaHash.StartsWith($"{PrefixoPbkdf2Sha256}$", StringComparison.Ordinal))
            return VerificarPbkdf2Sha256(senha, senhaHash);

        try
        {
            return _passwordHasher.VerifyHashedPassword(UsuarioHash, senhaHash, senha)
                is PasswordVerificationResult.Success
                    or PasswordVerificationResult.SuccessRehashNeeded;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool VerificarPbkdf2Sha256(string senha, string senhaHash)
    {
        var partes = senhaHash.Split('$', StringSplitOptions.None);
        if (partes.Length != 4
            || partes[0] != PrefixoPbkdf2Sha256
            || !int.TryParse(partes[1], out var iteracoes)
            || iteracoes is <= 0 or > MaximoIteracoesAceitas)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(partes[2]);
            var hashEsperado = Convert.FromBase64String(partes[3]);
            if (salt.Length == 0 || hashEsperado.Length == 0)
                return false;

            var hashCalculado = Rfc2898DeriveBytes.Pbkdf2(
                senha,
                salt,
                iteracoes,
                HashAlgorithmName.SHA256,
                hashEsperado.Length);

            return CryptographicOperations.FixedTimeEquals(hashCalculado, hashEsperado);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
