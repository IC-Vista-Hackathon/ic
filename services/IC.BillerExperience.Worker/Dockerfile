# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json NuGet.config Directory.Build.props Directory.Packages.props ./
COPY contracts/IC.BillerExperience.Contracts/IC.BillerExperience.Contracts.csproj contracts/IC.BillerExperience.Contracts/
COPY libraries/IC.Agentic.Orchestration/IC.Agentic.Orchestration.csproj libraries/IC.Agentic.Orchestration/
COPY services/IC.BillerExperience.Worker/IC.BillerExperience.Worker.csproj services/IC.BillerExperience.Worker/
RUN dotnet restore services/IC.BillerExperience.Worker/IC.BillerExperience.Worker.csproj
COPY contracts/IC.BillerExperience.Contracts/ contracts/IC.BillerExperience.Contracts/
COPY libraries/IC.Agentic.Orchestration/ libraries/IC.Agentic.Orchestration/
COPY services/IC.BillerExperience.Worker/ services/IC.BillerExperience.Worker/
RUN dotnet publish services/IC.BillerExperience.Worker/IC.BillerExperience.Worker.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "IC.BillerExperience.Worker.dll"]
