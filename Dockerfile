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
RUN dotnet publish Server/Server.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# TLS и sticky sessions делает Traefik; контейнеру достаточно одного HTTP listener.
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "Server.dll"]
