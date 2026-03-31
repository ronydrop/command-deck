@echo off
title Command Deck
echo Iniciando Command Deck...
echo.

REM Verificar se .NET 8 SDK esta instalado
dotnet --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERRO] .NET 8 SDK nao encontrado.
    echo Baixe em: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

echo Limpando build anterior...
dotnet clean src\CommandDeck\CommandDeck.csproj >nul 2>&1

echo Restaurando dependencias...
dotnet restore src\CommandDeck\CommandDeck.csproj

echo.
echo Compilando e iniciando...
dotnet run --project src\CommandDeck\CommandDeck.csproj
pause
