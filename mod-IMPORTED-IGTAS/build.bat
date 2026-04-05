@echo off
set "REPO_URL=https://github.com/pseudo-psychic/IGTAS.git"
set "SRC_DIR=%~dp0src"

echo === Building IGTAS (imported) ===

REM Use .NET 10 SDK if installed in user dir
if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    set "PATH=%USERPROFILE%\.dotnet;%PATH%"
    set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
)

if exist "%SRC_DIR%\.git" (
    echo Updating IGTAS repo...
    git -C "%SRC_DIR%" pull --ff-only
) else (
    echo Cloning IGTAS repo...
    git clone "%REPO_URL%" "%SRC_DIR%"
)

dotnet build -c Release "%SRC_DIR%\IGTAS.csproj"
if errorlevel 1 exit /b 1

REM Stage output
set "OUT_DIR=%~dp0bin\Release\netstandard2.1"
mkdir "%OUT_DIR%" 2>nul
copy /y "%SRC_DIR%\bin\Release\net472\IGTAS.dll" "%OUT_DIR%\"
