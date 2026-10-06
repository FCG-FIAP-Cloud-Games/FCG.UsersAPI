using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FCG.Users.Api.Configuration;

internal sealed class SegurancaSwaggerOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operacao, OperationFilterContext contexto)
    {
        var metadados = contexto.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (metadados.OfType<IAllowAnonymous>().Any() || !metadados.OfType<IAuthorizeData>().Any())
            return;

        // Apenas operações protegidas recebem o requisito; login/refresh permanecem públicos.
        operacao.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", contexto.Document)] = []
            }
        ];
    }
}
