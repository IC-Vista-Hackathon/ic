# Build context = repo root: docker build -f services/Pronto.Payment.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props NuGet.config global.json ./
COPY contracts/Pronto.Invoice.Contracts/Pronto.Invoice.Contracts.csproj contracts/Pronto.Invoice.Contracts/
COPY contracts/Pronto.Payment.Contracts/Pronto.Payment.Contracts.csproj contracts/Pronto.Payment.Contracts/
COPY libraries/Pronto.Persistence.Cosmos/Pronto.Persistence.Cosmos.csproj libraries/Pronto.Persistence.Cosmos/
COPY libraries/Pronto.ServiceDefaults/Pronto.ServiceDefaults.csproj libraries/Pronto.ServiceDefaults/
COPY services/Pronto.Payment.Api/Pronto.Payment.Api.csproj services/Pronto.Payment.Api/
RUN dotnet restore services/Pronto.Payment.Api/Pronto.Payment.Api.csproj
COPY contracts/ contracts/
COPY libraries/ libraries/
COPY services/Pronto.Payment.Api/ services/Pronto.Payment.Api/
RUN dotnet publish services/Pronto.Payment.Api/Pronto.Payment.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Pronto.Payment.Api.dll"]
