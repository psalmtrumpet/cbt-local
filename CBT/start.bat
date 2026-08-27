@echo off
title CBT Local Server

echo.
echo  ============================================
echo   NCS CBT - Local / Intranet Server
echo  ============================================
echo.

REM Check .env exists
if not exist ".env" (
    echo  ERROR: .env file not found.
    echo  Copy .env.example to .env and fill in your settings.
    echo.
    pause
    exit /b 1
)

REM Get local IP and show it
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /i "IPv4"') do (
    set IP=%%a
    goto :found_ip
)
:found_ip
set IP=%IP: =%

echo  Your local IP: %IP%
echo  Intranet users will access: http://%IP%:8080
echo.
echo  Make sure LOCAL_IP=%IP% is set in your .env file.
echo.

REM Open firewall port 8080 (requires admin — silently fails if not admin)
netsh advfirewall firewall add rule name="CBT Local Port 8080" dir=in action=allow protocol=TCP localport=8080 >nul 2>&1

echo  Starting containers...
docker compose up -d --build

if %errorlevel% neq 0 (
    echo.
    echo  ERROR: Docker failed. Is Docker Desktop running?
    pause
    exit /b 1
)

echo.
echo  ============================================
echo   CBT is running!
echo   Open: http://localhost:8080
echo   Intranet: http://%IP%:8080
echo  ============================================
echo.
echo  Press any key to view logs (Ctrl+C to stop watching)...
pause >nul
docker compose logs -f web
