using System.Security.Cryptography;
using FCG.Users.Application.Abstractions.Security;

namespace FCG.Users.Infrastructure.Security;

public sealed class HashSenhaPbkdf2 : IHashSenha
{
    private const int Iteracoes = 100_000;
    private const int TamanhoSalt = 16;
    private const int TamanhoHash = 32;

    public string Criar(string senha)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(senha);

        var salt = RandomNumberGenerator.GetBytes(TamanhoSalt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            senha,
            salt,
            Iteracoes,
            HashAlgorithmName.SHA256,
            TamanhoHash);

        return $"PBKDF2-SHA256${Iteracoes}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
}
