# Build context = repo root: docker build -f services/IC.PayerAccount.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props NuGet.config global.json ./
COPY contracts/IC.PayerAccount.Contracts/IC.PayerAccount.Contracts.csproj contracts/IC.PayerAccount.Contracts/
COPY libraries/IC.ServiceDefaults/IC.ServiceDefaults.csproj libraries/IC.ServiceDefaults/
COPY services/IC.PayerAccount.Api/IC.PayerAccount.Api.csproj services/IC.PayerAccount.Api/
RUN dotnet restore services/IC.PayerAccount.Api/IC.PayerAccount.Api.csproj
COPY contracts/ contracts/
COPY libraries/ libraries/
COPY services/IC.PayerAccount.Api/ services/IC.PayerAccount.Api/
RUN dotnet publish services/IC.PayerAccount.Api/IC.PayerAccount.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "IC.PayerAccount.Api.dll"]
