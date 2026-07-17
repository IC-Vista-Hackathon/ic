# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json NuGet.config Directory.Build.props Directory.Packages.props ./
COPY libraries/Pronto.ServiceDefaults/Pronto.ServiceDefaults.csproj libraries/Pronto.ServiceDefaults/
COPY services/Pronto.PayerExperience.Router/Pronto.PayerExperience.Router.csproj services/Pronto.PayerExperience.Router/
RUN dotnet restore services/Pronto.PayerExperience.Router/Pronto.PayerExperience.Router.csproj
COPY libraries/Pronto.ServiceDefaults/ libraries/Pronto.ServiceDefaults/
COPY services/Pronto.PayerExperience.Router/ services/Pronto.PayerExperience.Router/
RUN dotnet publish services/Pronto.PayerExperience.Router/Pronto.PayerExperience.Router.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish ./
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Pronto.PayerExperience.Router.dll"]
