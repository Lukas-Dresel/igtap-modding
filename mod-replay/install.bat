@echo off
setlocal
call "%~dp0..\_common.bat" %1
if errorlevel 1 exit /b 1

REM Install dependencies first
call "%~dp0..\mod\install.bat" "%GAME_DIR%"
if errorlevel 1 exit /b 1
call "%~dp0..\mod-fixedtimestep\install.bat" "%GAME_DIR%"
if errorlevel 1 exit /b 1

call "%~dp0build.bat"
if errorlevel 1 exit /b 1

mkdir "%PLUGINS_DIR%" 2>nul
copy /y "%~dp0bin\Release\netstandard2.1\IGTAPReplay.dll" "%PLUGINS_DIR%\"
echo Installed IGTAPReplay.dll
