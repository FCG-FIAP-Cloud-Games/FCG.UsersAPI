namespace FCG.Users.Api.Configuration;

internal static class ConfiguracaoDetalhesProblema
{
    public static IServiceCollection AdicionarDetalhesProblema(this IServiceCollection servicos)
    {
        servicos.AddProblemDetails(opcoes =>
        {
            opcoes.CustomizeProblemDetails = contexto =>
            {
                contexto.ProblemDetails.Instance ??= contexto.HttpContext.Request.Path;
                contexto.ProblemDetails.Extensions.TryAdd(
                    "traceId",
                    contexto.HttpContext.TraceIdentifier);

                if (contexto.ProblemDetails.Status == StatusCodes.Status500InternalServerError)
                {
                    contexto.ProblemDetails.Title = "Erro interno no servidor.";
                    contexto.ProblemDetails.Detail = "Ocorreu um erro inesperado ao processar a solicitação.";
                }
            };
        });

        return servicos;
    }
}
