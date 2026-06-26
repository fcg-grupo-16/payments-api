# syntax=docker/dockerfile:1
# Multi-stage build otimizado para produção.

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore (cacheável): copia só os csproj/sln primeiro
COPY ["PaymentsApi.sln", "./"]
COPY ["src/", "src/"]
RUN dotnet restore "src/Fcg.Payments.Api/Fcg.Payments.Api.csproj"

# Publica
RUN dotnet publish "src/Fcg.Payments.Api/Fcg.Payments.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Fcg.Payments.Api.dll"]
