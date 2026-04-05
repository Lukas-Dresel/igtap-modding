@echo off
set "REPO_URL=https://github.com/Rostmoment/HitboxViewer.git"
set "SRC_DIR=%~dp0src"

echo === Building HitboxViewer (imported) ===

REM Use .NET 10 SDK if installed in user dir
if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    set "PATH=%USERPROFILE%\.dotnet;%PATH%"
    set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
)

if exist "%SRC_DIR%\.git" (
    echo Updating HitboxViewer repo...
    git -C "%SRC_DIR%" pull --ff-only
) else (
    echo Cloning HitboxViewer repo...
    git clone "%REPO_URL%" "%SRC_DIR%"
)

dotnet build -c Release "%SRC_DIR%\HitboxViewer.sln"
if errorlevel 1 exit /b 1

REM Stage output to standard location
set "OUT_DIR=%~dp0bin\Release\netstandard2.1"
mkdir "%OUT_DIR%" 2>nul
copy /y "%SRC_DIR%\bin\Release\net35\HitboxViewer.dll" "%OUT_DIR%\"
copy /y "%SRC_DIR%\bin\Release\net35\UniverseLib*.dll" "%OUT_DIR%\"
