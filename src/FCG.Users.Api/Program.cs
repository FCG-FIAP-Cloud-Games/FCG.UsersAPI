using FCG.Users.Infrastructure.Repositories;
using FCG.Users.Api.Configuration;
using FCG.Users.Application.Auth;
using FCG.Users.Application.Usuarios;
using FCG.Users.Infrastructure;
using FCG.Users.Infrastructure.Security;

var inicializarAdministrador = args.Contains("--inicializar-admin-local", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args.Where(argumento => argumento != "--inicializar-admin-local").ToArray());
if (inicializarAdministrador)
    InicializacaoAdministradorLocal.ValidarAmbiente(builder.Configuration, builder.Environment);

builder.Services.AdicionarPersistencia(builder.Configuration);
builder.Services.AdicionarSegurancaCadastros();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ManipuladorCriarUsuario>();
if (inicializarAdministrador)
{
    builder.Services.AddScoped<InicializadorAdministradorLocal>();
    await using var inicializacao = builder.Build();
    Environment.ExitCode = await InicializacaoAdministradorLocal.ExecutarAsync(inicializacao.Services, builder.Configuration);
    return;
}

builder.Services.AddScoped<ManipuladorObterUsuario>();
builder.Services.AddScoped<ManipuladorAtualizarUsuario>();
builder.Services.AddScoped<ManipuladorAlterarPerfilUsuario>();
builder.Services.AddScoped<ManipuladorInativarUsuario>();
builder.Services.AddScoped<ManipuladorLogin>();
builder.Services.AddScoped<ManipuladorRenovarToken>();
builder.Services.AddScoped<ManipuladorLogout>();

builder.Services.AdicionarSegurancaAutenticacao(builder.Configuration);
builder.Services.AdicionarAutenticacaoJwt();
builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AdicionarDetalhesProblema();
builder.Services.AdicionarDocumentacaoSwagger();

var app = builder.Build();

// Falha na inicialização se as chaves estiverem ausentes, inválidas ou inconsistentes.
_ = app.Services.GetRequiredService<ChavesJwtRsa>();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UsarDocumentacaoSwagger();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Permite que WebApplicationFactory inicie este host nos testes HTTP.
public partial class Program
{
}
