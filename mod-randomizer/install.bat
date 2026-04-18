@echo off
setlocal
call "%~dp0..\_common.bat" %1
if errorlevel 1 exit /b 1

REM Ensure core mod is installed (we depend on it)
if not exist "%PLUGINS_DIR%\IGTAPMod.dll" (
    echo Core mod not found, installing...
    call "%~dp0..\mod\install.bat" "%GAME_DIR%"
    if errorlevel 1 exit /b 1
)

call "%~dp0build.bat"
if errorlevel 1 exit /b 1

mkdir "%PLUGINS_DIR%" 2>nul
copy /y "%~dp0bin\Release\netstandard2.1\IGTAPRandomizer.dll" "%PLUGINS_DIR%\"
echo Installed IGTAPRandomizer.dll
