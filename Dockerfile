FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/PaRiMerchant.Api/PaRiMerchant.Api.csproj src/PaRiMerchant.Api/
COPY src/PaRiMerchant.Application/PaRiMerchant.Application.csproj src/PaRiMerchant.Application/
COPY src/PaRiMerchant.Infrastructure/PaRiMerchant.Infrastructure.csproj src/PaRiMerchant.Infrastructure/
COPY src/PaRiMerchant.Domain/PaRiMerchant.Domain.csproj src/PaRiMerchant.Domain/
RUN dotnet restore src/PaRiMerchant.Api/PaRiMerchant.Api.csproj

COPY src ./src
RUN dotnet publish src/PaRiMerchant.Api/PaRiMerchant.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

CMD ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet PaRiMerchant.Api.dll"]
