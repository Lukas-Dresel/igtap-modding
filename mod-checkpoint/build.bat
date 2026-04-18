@echo off
if "%GAME_DIR%"=="" (
    call "%~dp0..\_common.bat" %1
    if errorlevel 1 exit /b 1
)
echo === Building IGTAPCheckpoint ===
dotnet build -c Release "%~dp0Checkpoint.csproj"
