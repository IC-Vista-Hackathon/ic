# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json NuGet.config Directory.Build.props Directory.Packages.props ./
COPY contracts/Pronto.BillerExperience.Contracts/Pronto.BillerExperience.Contracts.csproj contracts/Pronto.BillerExperience.Contracts/
COPY libraries/Pronto.Agentic.Orchestration/Pronto.Agentic.Orchestration.csproj libraries/Pronto.Agentic.Orchestration/
COPY services/Pronto.BillerExperience.Api/Pronto.BillerExperience.Api.csproj services/Pronto.BillerExperience.Api/
RUN dotnet restore services/Pronto.BillerExperience.Api/Pronto.BillerExperience.Api.csproj
COPY contracts/Pronto.BillerExperience.Contracts/ contracts/Pronto.BillerExperience.Contracts/
COPY libraries/Pronto.Agentic.Orchestration/ libraries/Pronto.Agentic.Orchestration/
COPY services/Pronto.BillerExperience.Api/ services/Pronto.BillerExperience.Api/
RUN dotnet publish services/Pronto.BillerExperience.Api/Pronto.BillerExperience.Api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "Pronto.BillerExperience.Api.dll"]
