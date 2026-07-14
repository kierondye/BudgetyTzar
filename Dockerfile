ARG DOTNET_VERSION=9.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-bookworm-slim AS restore
WORKDIR /src

COPY Directory.Build.props Directory.Build.targets BudgetyTzar.sln ./
COPY src/BudgetyTzar.Api/BudgetyTzar.Api.csproj src/BudgetyTzar.Api/
RUN dotnet restore src/BudgetyTzar.Api/BudgetyTzar.Api.csproj

FROM restore AS publish
COPY . .
RUN dotnet publish src/BudgetyTzar.Api/BudgetyTzar.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM restore AS ef-tools
COPY . .
RUN dotnet tool restore
ENTRYPOINT ["dotnet", "tool", "run", "dotnet-ef", "database", "update", "--project", "src/BudgetyTzar.Api", "--startup-project", "src/BudgetyTzar.Api", "--context", "ApplicationDbContext"]

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-noble-chiseled AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

COPY --from=publish --chown=app:app /app/publish ./

USER app
ENTRYPOINT ["dotnet", "BudgetyTzar.Api.dll"]
