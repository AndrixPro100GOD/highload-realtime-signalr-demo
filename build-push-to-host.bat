@echo off
setlocal
chcp 65001 >nul

set "CONTEXT=host"
set "COMPOSE_FILES=-f docker-compose.yml -f docker-compose.myhost.yml"

echo ========================================
echo Build + Push to Host
echo ========================================

:: Проверка и создание контекста
docker context inspect %CONTEXT% >nul 2>&1
if %errorlevel% neq 0 (
    echo Контекст %CONTEXT% не найден. Создаём...
    docker context create %CONTEXT% --docker "host=tcp://192.168.0.90:2375"
)

docker context use %CONTEXT%
echo Текущий контекст: %CONTEXT%
echo.

echo [1/3] Building images...
docker compose %COMPOSE_FILES% build
if %errorlevel% neq 0 goto fail

echo.
echo [2/3] Pushing images to host...
docker compose %COMPOSE_FILES% push
if %errorlevel% neq 0 goto fail

echo.
echo SUCCESS: Образы успешно собраны и отправлены на хост.
pause
exit /b 0

:fail
echo.
echo FAILED: Ошибка при выполнении.
pause
exit /b 1