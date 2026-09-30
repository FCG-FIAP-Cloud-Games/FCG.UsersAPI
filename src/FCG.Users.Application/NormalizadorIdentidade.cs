using System.Net.Mail;
using FCG.Users.Domain.Entities;

namespace FCG.Users.Application;

internal static class NormalizadorIdentidade
{
    public static string NormalizarNome(string? nome) =>
        string.Join(
            ' ',
            (nome ?? string.Empty).Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static string? NormalizarEmail(string? email)
    {
        var valor = email?.Trim();

        if (string.IsNullOrWhiteSpace(valor)
            || valor.Length > Usuario.TamanhoMaximoEmail
            || !MailAddress.TryCreate(valor, out var endereco)
            || !string.Equals(endereco.Address, valor, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return endereco.Address.ToLowerInvariant();
    }
}
