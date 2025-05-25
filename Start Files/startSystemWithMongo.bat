@echo off
echo ========================================
echo    MongoDB + RabbitMQ System Test
echo ========================================
echo.

echo [1/4] Testing MongoDB Connection...
cd /d "%~dp0..\SimpleTest"
dotnet run
echo.

echo [2/4] Starting Server (with MongoDB integration)...
cd /d "%~dp0..\Servidor"
start "Servidor" cmd /k "dotnet run"
timeout /t 3 /nobreak >nul

echo [3/4] Starting Agregador...
cd /d "%~dp0..\Agregador"
start "Agregador" cmd /k "dotnet run"
timeout /t 3 /nobreak >nul

echo [4/4] Starting Wavy Client...
cd /d "%~dp0..\Wavy"
start "Wavy" cmd /k "dotnet run"

echo.
echo ✅ All components started!
echo.
echo Open MongoDB Compass to view data:
echo Connection: mongodb+srv://marracho:220902Francisco@sistemasdistribuidos.mkvei02.mongodb.net/
echo Database: SistemasDistribuidos
echo.
pause
