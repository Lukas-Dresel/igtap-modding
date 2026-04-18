@echo off
if "%GAME_DIR%"=="" (
    call "%~dp0..\_common.bat" %1
    if errorlevel 1 exit /b 1
)
REM Build compile-time dependency first (sorts after us alphabetically)
call "%~dp0..\mod-speedrun\build.bat"
if errorlevel 1 exit /b 1

echo === Building IGTAPReplay ===
dotnet build -c Release "%~dp0Replay.csproj"
