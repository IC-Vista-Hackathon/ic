# Build context = repo root: docker build -f services/IC.Payment.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props NuGet.config global.json ./
COPY contracts/IC.Invoice.Contracts/IC.Invoice.Contracts.csproj contracts/IC.Invoice.Contracts/
COPY contracts/IC.Payment.Contracts/IC.Payment.Contracts.csproj contracts/IC.Payment.Contracts/
COPY libraries/IC.Persistence.Cosmos/IC.Persistence.Cosmos.csproj libraries/IC.Persistence.Cosmos/
COPY libraries/IC.ServiceDefaults/IC.ServiceDefaults.csproj libraries/IC.ServiceDefaults/
COPY services/IC.Payment.Api/IC.Payment.Api.csproj services/IC.Payment.Api/
RUN dotnet restore services/IC.Payment.Api/IC.Payment.Api.csproj
COPY contracts/ contracts/
COPY libraries/ libraries/
COPY services/IC.Payment.Api/ services/IC.Payment.Api/
RUN dotnet publish services/IC.Payment.Api/IC.Payment.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "IC.Payment.Api.dll"]
