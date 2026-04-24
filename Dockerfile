# Многостадийная сборка: SDK собирает Blazor WASM, nginx раздаёт только wwwroot (легковесный рантайм для демо).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Копируем проект и восстанавливаем зависимости отдельным слоем для кэша Docker.
COPY highload-realtime-signalr-demo.csproj .
RUN dotnet restore highload-realtime-signalr-demo.csproj

COPY . .
RUN dotnet publish highload-realtime-signalr-demo.csproj -c Release -o /app/publish --no-restore

FROM nginx:1.27-alpine

# Убираем дефолтный конфиг, подключаем свой (SPA + кэш ассетов).
RUN rm /etc/nginx/conf.d/default.conf
COPY docker/nginx.conf /etc/nginx/conf.d/default.conf

COPY --from=build /app/publish/wwwroot /usr/share/nginx/html

EXPOSE 80
