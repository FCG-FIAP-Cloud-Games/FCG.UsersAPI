# Compilação: o professor não precisa instalar o SDK na máquina.
FROM mcr.microsoft.com/dotnet/sdk:8.0.423-bookworm-slim AS build
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/FCG.Users.Domain/FCG.Users.Domain.csproj src/FCG.Users.Domain/
COPY src/FCG.Users.Application/FCG.Users.Application.csproj src/FCG.Users.Application/
COPY src/FCG.Users.Infrastructure/FCG.Users.Infrastructure.csproj src/FCG.Users.Infrastructure/
COPY src/FCG.Users.Api/FCG.Users.Api.csproj src/FCG.Users.Api/
COPY tools/FCG.Users.DemoSetup/FCG.Users.DemoSetup.csproj tools/FCG.Users.DemoSetup/
RUN dotnet restore src/FCG.Users.Api/FCG.Users.Api.csproj \
    && dotnet restore tools/FCG.Users.DemoSetup/FCG.Users.DemoSetup.csproj
COPY src/ src/
COPY tools/FCG.Users.DemoSetup/ tools/FCG.Users.DemoSetup/
RUN dotnet publish src/FCG.Users.Api/FCG.Users.Api.csproj -c Release --no-restore -o /out/api /p:UseAppHost=false \
    && dotnet publish tools/FCG.Users.DemoSetup/FCG.Users.DemoSetup.csproj -c Release --no-restore -o /out/preparacao /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0.31-bookworm-slim AS runtime
# curl é usado somente pela verificação de vida do container.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data/jwt /data/jwt-publica \
    && chown app:app /data/jwt /data/jwt-publica \
    && chmod 0700 /data/jwt \
    && chmod 0755 /data/jwt-publica
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DemonstracaoLocal__Habilitada=false
# app mantém o usuário não privilegiado (UID 1654) da imagem oficial .NET.
USER app

# Alvo legado: permanece disponível para as provas/integrações que usam o job separado.
FROM runtime AS preparacao
COPY --from=build /out/preparacao/ ./
ENTRYPOINT ["dotnet", "FCG.Users.DemoSetup.dll", "--preparar-demonstracao"]

# Uma imagem distribui API e preparador; a flag explícita controla o bootstrap local.
FROM runtime AS api
COPY --from=build /out/api/ ./
COPY --from=build /out/preparacao/ ./preparacao/
COPY --chmod=0555 infra/runtime/docker-entrypoint.sh /app/docker-entrypoint.sh
EXPOSE 8080
HEALTHCHECK --interval=5s --timeout=3s --start-period=90s --retries=12 \
    CMD curl --fail --silent --output /dev/null http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["/app/docker-entrypoint.sh"]
