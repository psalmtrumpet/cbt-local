@echo off
title CBT Local - Build

echo.
echo  ============================================
echo   NCS CBT - Build (requires internet)
echo  ============================================
echo.
echo  This pulls base images and builds the app.
echo  Run this at home before going to the venue.
echo.

if not exist ".env" (
    echo  ERROR: .env file not found.
    echo  Copy .env.example to .env and fill in your settings.
    echo.
    pause
    exit /b 1
)

echo  Building...
docker compose build --pull

if %errorlevel% neq 0 (
    echo.
    echo  ERROR: Build failed. Check your internet connection.
    pause
    exit /b 1
)

echo.
echo  ============================================
echo   Build complete! You can now take this
echo   laptop to the venue and run start.bat
echo   without needing internet.
echo  ============================================
echo.
pause
