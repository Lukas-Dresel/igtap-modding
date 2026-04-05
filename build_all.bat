@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
set "FAILED=0"

for /d %%M in ("%SCRIPT_DIR%mod*") do (
    if exist "%%M\build.bat" (
        call "%%M\build.bat"
        if errorlevel 1 set "FAILED=1"
        echo.
    )
)

if "%FAILED%"=="0" (
    echo === All builds succeeded ===
) else (
    echo === Some builds failed ===
    exit /b 1
)
