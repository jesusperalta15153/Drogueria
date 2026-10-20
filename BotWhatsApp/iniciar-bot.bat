@echo off
title Bot WhatsApp - Drogueria FarmaVida
color 0A

:: Ubicarse en la carpeta del bot
cd /d "%~dp0"

:INICIO
cls
echo ======================================================
echo    BOT DE WHATSAPP - DROGUERIA FARMAVIDA
echo    Numero configurado: +57 313 595 8228
echo ======================================================
echo.

:: Verificar si existen las dependencias
if not exist node_modules (
    echo [INFO] Instalando dependencias por primera vez...
    call npm install
    echo.
)

echo Selecciona una opcion:
echo.
echo   [1] Iniciar con Codigo QR (Escanear rapido con la camara)
echo   [2] Iniciar con Codigo de 8 digitos (Vincular con numero de telefono)
echo   [3] Iniciar normal (Si ya lo habias vinculado)
echo   [4] Limpiar sesion (Borrar datos viejos y empezar de cero)
echo.
set /p OPCION="Escribe el numero de tu opcion [1, 2, 3 o 4] y pulsa Enter: "

if "%OPCION%"=="1" goto MODO_QR
if "%OPCION%"=="2" goto MODO_CODE
if "%OPCION%"=="3" goto MODO_NORMAL
if "%OPCION%"=="4" goto LIMPIAR

echo.
echo Opcion no valida. Iniciando en Modo QR por defecto...
goto MODO_QR

:MODO_QR
cls
echo ======================================================
echo    INICIANDO BOT EN MODO CODIGO QR...
echo ======================================================
echo Escanea el codigo con tu WhatsApp desde "Dispositivos vinculados".
echo.
node index.js
pause
goto INICIO

:MODO_CODE
cls
echo ======================================================
echo    INICIANDO BOT EN MODO CODIGO DE 8 DIGITOS...
echo    Numero: +57 313 595 8228
echo ======================================================
echo Espera unos segundos mientras WhatsApp genera el codigo...
echo.
node index.js --code
pause
goto INICIO

:MODO_NORMAL
cls
echo ======================================================
echo    CONECTANDO BOT...
echo ======================================================
echo.
node index.js
pause
goto INICIO

:LIMPIAR
cls
echo ======================================================
echo    LIMPIANDO SESION ANTERIOR...
echo ======================================================
if exist auth_info_baileys rmdir /s /q auth_info_baileys
if exist codigo-qr.png del /f /q codigo-qr.png
echo.
echo Sesion borrada correctamente.
echo Presiona cualquier tecla para volver al menu...
pause >nul
goto INICIO
