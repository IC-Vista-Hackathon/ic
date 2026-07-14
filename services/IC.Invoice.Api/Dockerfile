# Build context = repo root: docker build -f services/IC.Invoice.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props NuGet.config global.json ./
COPY contracts/IC.Invoice.Contracts/IC.Invoice.Contracts.csproj contracts/IC.Invoice.Contracts/
COPY libraries/IC.ServiceDefaults/IC.ServiceDefaults.csproj libraries/IC.ServiceDefaults/
COPY services/IC.Invoice.Api/IC.Invoice.Api.csproj services/IC.Invoice.Api/
RUN dotnet restore services/IC.Invoice.Api/IC.Invoice.Api.csproj
COPY contracts/ contracts/
COPY libraries/ libraries/
COPY services/IC.Invoice.Api/ services/IC.Invoice.Api/
RUN dotnet publish services/IC.Invoice.Api/IC.Invoice.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "IC.Invoice.Api.dll"]
