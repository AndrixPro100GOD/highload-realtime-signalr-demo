# Многостадийная сборка: отдельный лёгкий REST API не тащит SignalR hot path в свой образ.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Кэшируем restore отдельно: при правках кода API зависимости не скачиваются заново.
COPY Api/Api.csproj Api/
RUN dotnet restore Api/Api.csproj

COPY Api/ Api/
RUN dotnet publish Api/Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl нужен compose healthcheck'ам; без него runtime-образ ASP.NET Core слишком минимален.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "Api.dll"]
