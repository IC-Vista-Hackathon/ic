# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json NuGet.config Directory.Build.props Directory.Packages.props ./
COPY services/Pronto.PayerExperience.Router/Pronto.PayerExperience.Router.csproj services/Pronto.PayerExperience.Router/
RUN dotnet restore services/Pronto.PayerExperience.Router/Pronto.PayerExperience.Router.csproj
COPY services/Pronto.PayerExperience.Router/ services/Pronto.PayerExperience.Router/
RUN dotnet publish services/Pronto.PayerExperience.Router/Pronto.PayerExperience.Router.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "Pronto.PayerExperience.Router.dll"]
