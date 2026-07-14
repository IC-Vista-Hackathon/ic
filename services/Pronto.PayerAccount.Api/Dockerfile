# Build context = repo root: docker build -f services/Pronto.PayerAccount.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props NuGet.config global.json ./
COPY contracts/Pronto.PayerAccount.Contracts/Pronto.PayerAccount.Contracts.csproj contracts/Pronto.PayerAccount.Contracts/
COPY libraries/Pronto.Persistence.Cosmos/Pronto.Persistence.Cosmos.csproj libraries/Pronto.Persistence.Cosmos/
COPY libraries/Pronto.ServiceDefaults/Pronto.ServiceDefaults.csproj libraries/Pronto.ServiceDefaults/
COPY services/Pronto.PayerAccount.Api/Pronto.PayerAccount.Api.csproj services/Pronto.PayerAccount.Api/
RUN dotnet restore services/Pronto.PayerAccount.Api/Pronto.PayerAccount.Api.csproj
COPY contracts/ contracts/
COPY libraries/ libraries/
COPY services/Pronto.PayerAccount.Api/ services/Pronto.PayerAccount.Api/
RUN dotnet publish services/Pronto.PayerAccount.Api/Pronto.PayerAccount.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Pronto.PayerAccount.Api.dll"]
