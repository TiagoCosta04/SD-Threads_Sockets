@echo off
REM Run this to start all three components simultaneously

start cmd /k "cd %~dp0\..\Wavy && dotnet run"

start cmd /k "cd %~dp0\..\Agregador && dotnet run"

start cmd /k "cd %~dp0\..\Servidor && dotnet run"

close