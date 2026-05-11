@echo off
chcp 65001 >nul
title Build & Push to Host

echo ========================================
echo Сборка на ПК → Пуш на хост
echo ========================================

:: Проверяем, запущен ли Docker Desktop
docker version >nul 2>&1
if %errorlevel% neq 0 (
    echo [ОШИБКА] Docker Desktop не запущен!   
    pause
    exit /b 1
)

set "COMPOSE_FILES=-f docker-compose.yml -f docker-compose.myhost.yml"

echo [1/2] Building images on your PC...
docker compose %COMPOSE_FILES% build
if %errorlevel% neq 0 goto fail

echo.
echo [2/2] Pushing images to host registry...
docker compose %COMPOSE_FILES% push
if %errorlevel% neq 0 goto fail

echo.
echo SUCCESS: Образы собраны и отправлены на хост!
pause
exit /b 0

:fail
echo.
echo FAILED: Ошибка при сборке или пушe.
pause
exit /b 1