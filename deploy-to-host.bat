@echo off
setlocal
chcp 65001 >nul

set "CONTEXT=host"
set "COMPOSE_FILES=-f docker-compose.yml -f docker-compose.myhost.yml"

echo ========================================
echo Deploy to Host
echo ========================================

docker context use %CONTEXT% >nul 2>&1
if %errorlevel% neq 0 (
    echo Контекст %CONTEXT% не найден!
    pause
    exit /b 1
)

echo Текущий контекст: %CONTEXT%
echo.

echo [1/2] Pulling images...
docker compose %COMPOSE_FILES% pull
if %errorlevel% neq 0 goto fail

echo.
echo [2/2] Starting stack...
docker compose %COMPOSE_FILES% up -d
if %errorlevel% neq 0 goto fail

echo.
echo SUCCESS: Стек успешно запущен на хосте.
docker compose %COMPOSE_FILES% ps
pause
exit /b 0

:fail
echo.
echo FAILED: Ошибка при деплое.
pause
exit /b 1