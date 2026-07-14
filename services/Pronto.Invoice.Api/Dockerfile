# syntax=docker/dockerfile:1
#
# Multi-stage image for Pronto.Invoice.Api.
# Build context is the REPO ROOT (the service references Pronto.Invoice.Contracts and
# relies on the root Directory.*.props / global.json), so build from the repo root:
#
#   docker build -f services/Pronto.Invoice.Api/Dockerfile -t ic-invoice-api .

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, using only the manifests. This layer is cached and re-runs only
# when the build config or a csproj changes — not on every source edit.
COPY global.json NuGet.config Directory.Build.props Directory.Packages.props ./
COPY contracts/Pronto.Invoice.Contracts/Pronto.Invoice.Contracts.csproj contracts/Pronto.Invoice.Contracts/
COPY libraries/Pronto.Persistence.Cosmos/Pronto.Persistence.Cosmos.csproj libraries/Pronto.Persistence.Cosmos/
COPY services/Pronto.Invoice.Api/Pronto.Invoice.Api.csproj services/Pronto.Invoice.Api/
RUN dotnet restore services/Pronto.Invoice.Api/Pronto.Invoice.Api.csproj

# Copy the actual sources and publish a Release build.
COPY contracts/Pronto.Invoice.Contracts/ contracts/Pronto.Invoice.Contracts/
COPY libraries/Pronto.Persistence.Cosmos/ libraries/Pronto.Persistence.Cosmos/
COPY services/Pronto.Invoice.Api/ services/Pronto.Invoice.Api/
RUN dotnet publish services/Pronto.Invoice.Api/Pronto.Invoice.Api.csproj \
    -c Release --no-restore -o /app/publish

# ---- runtime ----
# Chiseled (distroless-style) ASP.NET runtime: minimal, no shell/package manager,
# runs as a non-root user, listens on port 8080 by default.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "Pronto.Invoice.Api.dll"]
