using FCG.Users.Api.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AdicionarDetalhesProblema();
builder.Services.AdicionarDocumentacaoSwagger();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UsarDocumentacaoSwagger();
app.MapControllers();

app.Run();

// Permite que WebApplicationFactory inicie este host nos testes HTTP.
public partial class Program
{
}
