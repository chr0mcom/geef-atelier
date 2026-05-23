FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy central build config first (layer caching)
COPY Directory.Build.props Directory.Packages.props nuget.config ./

# Copy project files and restore
COPY src/Geef.Atelier.Core/Geef.Atelier.Core.csproj src/Geef.Atelier.Core/
COPY src/Geef.Atelier.Infrastructure/Geef.Atelier.Infrastructure.csproj src/Geef.Atelier.Infrastructure/
COPY src/Geef.Atelier.Web/Geef.Atelier.Web.csproj src/Geef.Atelier.Web/
COPY src/Geef.Atelier.Mcp/Geef.Atelier.Mcp.csproj src/Geef.Atelier.Mcp/
COPY Geef.Atelier.slnx ./
RUN dotnet restore src/Geef.Atelier.Web/Geef.Atelier.Web.csproj

# Copy source and publish
COPY src/ src/
RUN dotnet publish src/Geef.Atelier.Web/Geef.Atelier.Web.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

RUN groupadd -r appgroup && useradd -r -g appgroup appuser

COPY --from=build /app/publish .
# Pre-create volume mount points so fresh named volumes inherit appuser ownership
# (DataProtection keys must be writable by the non-root app user).
RUN mkdir -p /app/dataprotection-keys /app/exports \
    && chown -R appuser:appgroup /app

USER appuser

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Geef.Atelier.Web.dll"]
