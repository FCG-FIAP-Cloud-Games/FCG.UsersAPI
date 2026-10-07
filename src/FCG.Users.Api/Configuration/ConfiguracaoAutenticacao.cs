using Microsoft.AspNetCore.Authorization;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using FCG.Users.Domain.Entities;
using FCG.Users.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace FCG.Users.Api.Configuration;

internal static class ConfiguracaoAutenticacao
{
    public static IServiceCollection AdicionarAutenticacaoJwt(this IServiceCollection servicos)
    {
        servicos.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        servicos.AddAuthorization(opcoes => opcoes.AddPolicy(PoliticasUsuarios.TitularOuAdministrador,
            politica => politica.RequireAuthenticatedUser().AddRequirements(new RequisitoTitularOuAdministrador())));
        servicos.AddSingleton<IAuthorizationHandler, AutorizacaoTitularOuAdministrador>();
        servicos.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<ChavesJwtRsa>((opcoes, chaves) =>
            {
                // Preserva os nomes do contrato compartilhado: sub e role.
                opcoes.MapInboundClaims = false;
                opcoes.IncludeErrorDetails = false;
                opcoes.SaveToken = false;
                opcoes.TokenValidationParameters = chaves.CriarParametrosValidacao();
                opcoes.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ValidarIdentidadeAsync,
                    OnChallenge = ResponderDesafioAsync,
                    OnForbidden = ResponderProibidoAsync
                };
            });

        return servicos;
    }

    private static Task ValidarIdentidadeAsync(TokenValidatedContext contexto)
    {
        var subjects = contexto.Principal?.FindAll(JwtRegisteredClaimNames.Sub).ToArray();
        var perfis = contexto.Principal?.FindAll("role").ToArray();
        var instantes = contexto.Principal?.FindAll(JwtRegisteredClaimNames.Iat).ToArray();
        var identificadores = contexto.Principal?.FindAll(JwtRegisteredClaimNames.Jti).ToArray();

        if (subjects is not { Length: 1 }
            || !Guid.TryParse(subjects[0].Value, out var usuarioId)
            || usuarioId == Guid.Empty
            || perfis is not { Length: 1 }
            || perfis[0].Value is not (PerfisSistema.Usuario or PerfisSistema.Administrador)
            || instantes is not { Length: 1 }
            || !long.TryParse(instantes[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var emitidoEm)
            || emitidoEm <= 0
            || identificadores is not { Length: 1 }
            || string.IsNullOrWhiteSpace(identificadores[0].Value))
        {
            contexto.Fail("O token não contém uma identidade válida.");
        }

        return Task.CompletedTask;
    }

    private static async Task ResponderDesafioAsync(JwtBearerChallengeContext contexto)
    {
        contexto.HandleResponse();
        if (contexto.Response.HasStarted)
            return;

        contexto.Response.Headers.WWWAuthenticate = "Bearer";
        await Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Autenticação necessária.",
            detail: "Informe um token de acesso válido.")
            .ExecuteAsync(contexto.HttpContext);
    }

    private static async Task ResponderProibidoAsync(ForbiddenContext contexto)
    {
        if (contexto.Response.HasStarted)
            return;

        await Results.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "Acesso não permitido.",
            detail: "O usuário autenticado não tem permissão para esta operação.")
            .ExecuteAsync(contexto.HttpContext);
    }
}
