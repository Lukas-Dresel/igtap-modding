@echo off
REM Shared helpers for IGTAP mod install scripts.
REM Called by: call "%~dp0..\_common.bat" [game_path]
REM Sets: GAME_DIR, PLUGINS_DIR

set "GAME_NAME=IGTAP an Incremental Game That's Also a Platformer Demo"
set "BEPINEX_VERSION=5.4.23.5"

REM --- Find game directory ---
if not "%~1"=="" if exist "%~1" (
    set "GAME_DIR=%~1"
    goto :found_game
)

REM Common Steam paths
for %%D in (
    "C:\Program Files (x86)\Steam\steamapps\common\%GAME_NAME%"
    "C:\Program Files\Steam\steamapps\common\%GAME_NAME%"
    "D:\SteamLibrary\steamapps\common\%GAME_NAME%"
    "E:\SteamLibrary\steamapps\common\%GAME_NAME%"
) do (
    if exist "%%~D" (
        set "GAME_DIR=%%~D"
        goto :found_game
    )
)

echo ERROR: Could not find the game directory.
echo Usage: %0 [path\to\game]
echo Expected: %GAME_NAME%
exit /b 1

:found_game
echo Game: %GAME_DIR%
set "PLUGINS_DIR=%GAME_DIR%\BepInEx\plugins"

REM --- Install BepInEx if needed ---
if exist "%GAME_DIR%\BepInEx\core\BepInEx.dll" goto :bepinex_done

echo === Installing BepInEx %BEPINEX_VERSION% ===
set "ZIP_NAME=BepInEx_win_x64_%BEPINEX_VERSION%.zip"
set "URL=https://github.com/BepInEx/BepInEx/releases/download/v%BEPINEX_VERSION%/%ZIP_NAME%"
set "TMP_ZIP=%GAME_DIR%\%ZIP_NAME%"

where curl >nul 2>&1
if %errorlevel%==0 (
    curl -fSL -o "%TMP_ZIP%" "%URL%"
) else (
    powershell -Command "Invoke-WebRequest -Uri '%URL%' -OutFile '%TMP_ZIP%'"
)

if not exist "%TMP_ZIP%" (
    echo ERROR: Download failed.
    exit /b 1
)

powershell -Command "Expand-Archive -Force '%TMP_ZIP%' '%GAME_DIR%'"
del "%TMP_ZIP%"
mkdir "%PLUGINS_DIR%" 2>nul
echo BepInEx installed.

:bepinex_done
exit /b 0
