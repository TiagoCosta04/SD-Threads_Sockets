@echo off
echo Iniciando Wavy...
start cmd /k "cd %~dp0\Wavy && dotnet run"

echo Iniciando Agregador...
start cmd /k "cd %~dp0\Agregador && dotnet run"

echo Iniciando Servidor...
start cmd /k "cd %~dp0\Servidor && dotnet run"

echo Todos os componentes foram iniciados!
close