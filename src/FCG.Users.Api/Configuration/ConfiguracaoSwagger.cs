using Microsoft.OpenApi;

namespace FCG.Users.Api.Configuration;

internal static class ConfiguracaoSwagger
{
    private const string NomeDocumento = "v1";

    public static IServiceCollection AdicionarDocumentacaoSwagger(this IServiceCollection servicos)
    {
        servicos.AddEndpointsApiExplorer();
        servicos.AddSwaggerGen(opcoes =>
        {
            opcoes.SwaggerDoc(NomeDocumento, new OpenApiInfo
            {
                Title = "FCG UsersAPI",
                Version = NomeDocumento,
                Description = "Microsserviço de usuários e autenticação do FIAP Cloud Games."
            });
            opcoes.OperationFilter<SegurancaSwaggerOperationFilter>();
            opcoes.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Token de acesso emitido pelo login ou refresh. Logout exige Bearer; saúde, cadastro, login e refresh são públicos."
            });
        });

        return servicos;
    }

    public static void UsarDocumentacaoSwagger(this WebApplication aplicacao)
    {
        if (!aplicacao.Environment.IsDevelopment()
            || !aplicacao.Configuration.GetValue<bool>("Swagger:Enabled"))
        {
            return;
        }

        aplicacao.UseSwagger();
        aplicacao.UseSwaggerUI(opcoes =>
        {
            opcoes.SwaggerEndpoint($"/swagger/{NomeDocumento}/swagger.json", "FCG UsersAPI v1");
            opcoes.DisplayRequestDuration();
        });
    }
}
