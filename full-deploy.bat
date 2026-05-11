@echo off
chcp 65001 >nul
title Deploy to Host

echo ========================================
echo Full Deploy: Build → Push → Deploy
echo ========================================

set "CONTEXT=host"
set "COMPOSE_FILES=-f docker-compose.yml -f docker-compose.myhost.yml"

:: Проверка и создание контекста
docker context inspect %CONTEXT% >nul 2>&1
if %errorlevel% neq 0 (
    echo Создаём контекст %CONTEXT%...
    docker context create %CONTEXT% --docker "host=tcp://192.168.0.90:2375"
)

docker context use %CONTEXT%
echo Контекст: %CONTEXT%
echo.

echo [1/3] Building images...
docker compose %COMPOSE_FILES% build
if %errorlevel% neq 0 goto fail

echo.
echo [2/3] Pushing images to host...
docker compose %COMPOSE_FILES% push
if %errorlevel% neq 0 goto fail

echo.
echo [3/3] Deploying on host...
docker compose %COMPOSE_FILES% up -d
if %errorlevel% neq 0 goto fail

echo.
echo ========================================
echo SUCCESS: Всё выполнено!
echo ========================================
docker compose %COMPOSE_FILES% ps
pause
exit /b 0

:fail
echo.
echo FAILED: Ошибка на каком-то этапе.
pause
exit /b 1