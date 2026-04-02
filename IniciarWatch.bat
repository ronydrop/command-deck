@echo off
title Command Deck [Hot Reload]
echo Iniciando Command Deck com Hot Reload...
echo.
echo Salve os arquivos .cs para aplicar mudancas automaticamente.
echo Ctrl+C para encerrar.
echo.

dotnet --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERRO] .NET 8 SDK nao encontrado.
    pause
    exit /b 1
)

dotnet watch run --project src\CommandDeck\CommandDeck.csproj
pause
