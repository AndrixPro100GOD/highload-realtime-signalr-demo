# Многостадийная сборка: публикуем hosted Blazor WASM + ASP.NET Core SignalR server в один контейнер.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Кэшируем restore отдельно от исходников, чтобы ускорить rebuild.
COPY highload-realtime-signalr-demo.csproj ./
COPY Shared/Shared.csproj Shared/
COPY Server/Server.csproj Server/
COPY LoadTester/LoadTester.csproj LoadTester/

RUN dotnet restore Server/Server.csproj

COPY . .

# Docker performance: для локального high-load стенда отключаем Blazor WASM trimming/AOT,
# иначе publish может минутами висеть на "Optimizing assemblies for size".
RUN dotnet publish Server/Server.csproj -c Release -o /app/publish --no-restore \
    -p:PublishTrimmed=false \
    -p:RunAOTCompilation=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl нужен compose healthcheck'ам; реальный edge routing выполняют HAProxy и Nginx снаружи контейнера.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV DOTNET_EnableDiagnostics=0
ENV DOTNET_GCServer=1
ENV DOTNET_GCRetainVM=1
ENV DOTNET_TieredPGO=1
ENV DOTNET_TC_QuickJitForLoops=1

COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "Server.dll"]
