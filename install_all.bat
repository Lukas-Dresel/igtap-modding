@echo off
REM Usage: install_all.bat [-y] [path\to\game]
REM   -y    Auto-accept all prompts
setlocal enabledelayedexpansion

set "AUTO_YES=0"
set "GAME_ARG="
for %%A in (%*) do (
    if /i "%%A"=="-y" (set "AUTO_YES=1") else if /i "%%A"=="--yes" (set "AUTO_YES=1") else (set "GAME_ARG=%%A")
)

echo ============================================
echo   IGTAP Mod Installer
echo ============================================
echo.

set "SCRIPT_DIR=%~dp0"
call "%SCRIPT_DIR%_common.bat" !GAME_ARG!
if errorlevel 1 exit /b 1

echo.

REM --- BepInEx ---
if exist "%GAME_DIR%\BepInEx\core\BepInEx.dll" (
    echo BepInEx: already installed.
) else (
    echo --------------------------------------------
    echo BepInEx %BEPINEX_VERSION% ^(mod loader framework^)
    echo.
    echo   BepInEx is required for all mods to work.
    echo   This will:
    echo     - Download BepInEx_win_x64_%BEPINEX_VERSION%.zip from GitHub
    echo     - Extract it into the game directory
    echo     - Create BepInEx\core\, BepInEx\plugins\, etc.
    echo     - No game files are modified or overwritten
    echo --------------------------------------------
    call :prompt "Install BepInEx?"
    if "!ANSWER!"=="y" (
        call "%SCRIPT_DIR%_common.bat" "%GAME_DIR%"
        echo BepInEx installed successfully.
    ) else if "!ANSWER!"=="q" (
        goto :done
    ) else (
        echo WARNING: Skipping BepInEx. Mods will not work without it.
    )
)

mkdir "%PLUGINS_DIR%" 2>nul

REM --- Mods ---
set "INSTALLED="
set "SKIPPED="

for /d %%M in ("%SCRIPT_DIR%mod*") do (
    echo %%~nxM | findstr /i "IMPORTED" >nul || (
        if exist "%%M\install.bat" if exist "%%M\mod.info" (
            call :install_mod "%%M"
            if "!ANSWER!"=="q" goto :summary
        )
    )
)

:summary
echo.
echo ============================================
echo   Installation Summary
echo ============================================
if defined INSTALLED (
    echo.
    echo   Installed:
    for %%I in (!INSTALLED!) do echo     + %%~I
)
if defined SKIPPED (
    echo.
    echo   Skipped:
    for %%I in (!SKIPPED!) do echo     - %%~I
)
echo.
echo   Plugins directory:
dir /b "%PLUGINS_DIR%\*.dll" 2>nul
echo.
echo Set Steam launch options:
echo   "%GAME_DIR%\run_bepinex.sh" %%command%%
echo.
goto :done

:install_mod
set "MOD_DIR=%~1"
set "MOD_NAME="
set "MOD_DLL="
set "MOD_DESC="
set "MOD_ACTIONS="
for /f "usebackq tokens=1,* delims==" %%A in ("%MOD_DIR%\mod.info") do (
    if "%%A"=="name" set "MOD_NAME=%%B"
    if "%%A"=="dll" set "MOD_DLL=%%B"
    if "%%A"=="description" set "MOD_DESC=%%B"
    if "%%A"=="actions" set "MOD_ACTIONS=%%B"
)
echo.
echo --------------------------------------------
echo !MOD_NAME!
echo.
echo   !MOD_DESC!
echo.
echo   This will:
echo     - !MOD_ACTIONS!
if exist "%PLUGINS_DIR%\!MOD_DLL!" (
    echo.
    echo   ^(Currently installed -- will be overwritten with latest build^)
)
echo --------------------------------------------
call :prompt "Install !MOD_NAME!?"
if "!ANSWER!"=="y" (
    echo.
    call "%MOD_DIR%\install.bat" "%GAME_DIR%"
    set "INSTALLED=!INSTALLED! "!MOD_NAME!""
) else if "!ANSWER!"=="q" (
    exit /b
) else (
    set "SKIPPED=!SKIPPED! "!MOD_NAME!""
)
exit /b

:prompt
set "ANSWER="
if "!AUTO_YES!"=="1" (
    echo.
    echo %~1 -^> auto-accepted ^(-y^)
    set "ANSWER=y"
    exit /b
)
:prompt_loop
set /p "ANSWER=%~1 [y]es / [s]kip / [q]uit: "
if /i "!ANSWER!"=="y" exit /b
if /i "!ANSWER!"=="yes" set "ANSWER=y" & exit /b
if /i "!ANSWER!"=="s" exit /b
if /i "!ANSWER!"=="skip" set "ANSWER=s" & exit /b
if /i "!ANSWER!"=="q" exit /b
if /i "!ANSWER!"=="quit" set "ANSWER=q" & exit /b
echo   Please enter y, s, or q.
goto :prompt_loop

:done
endlocal
