using System.IdentityModel.Tokens.Jwt;
using FCG.Users.Domain.Entities;
using Microsoft.AspNetCore.Authorization;

namespace FCG.Users.Api.Configuration;

internal static class PoliticasUsuarios
{
    public const string TitularOuAdministrador = "TitularOuAdministrador";
}

internal sealed class RequisitoTitularOuAdministrador : IAuthorizationRequirement;

internal sealed class AutorizacaoTitularOuAdministrador : AuthorizationHandler<RequisitoTitularOuAdministrador, Guid>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext contexto,
        RequisitoTitularOuAdministrador requisito, Guid usuarioId)
    {
        if (contexto.User.Identity?.IsAuthenticated == true
            && (contexto.User.IsInRole(PerfisSistema.Administrador)
                || (Guid.TryParse(contexto.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var titular)
                    && titular == usuarioId)))
            contexto.Succeed(requisito);
        return Task.CompletedTask;
    }
}
