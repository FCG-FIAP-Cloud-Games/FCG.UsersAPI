using Microsoft.AspNetCore.Mvc.Filters;

namespace FCG.Users.Api.Configuration;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
internal sealed class NaoArmazenarRespostaAttribute : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext contexto, ResourceExecutionDelegate proximo)
    {
        // O recurso é filtrado antes do model binding; OnStarting também cobre o 400
        // automático e a resposta do middleware de exceções, que limpa os cabeçalhos.
        contexto.HttpContext.Response.OnStarting(() =>
        {
            contexto.HttpContext.Response.Headers.CacheControl = "no-store, no-cache";
            contexto.HttpContext.Response.Headers.Pragma = "no-cache";
            return Task.CompletedTask;
        });
        await proximo();
    }
}
