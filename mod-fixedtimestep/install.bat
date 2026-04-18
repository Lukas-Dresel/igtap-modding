@echo off
setlocal
call "%~dp0..\_common.bat" %1
if errorlevel 1 exit /b 1

call "%~dp0build.bat"
if errorlevel 1 exit /b 1

mkdir "%PLUGINS_DIR%" 2>nul
copy /y "%~dp0bin\Release\netstandard2.1\IGTAPFixedTimestep.dll" "%PLUGINS_DIR%\"
echo Installed IGTAPFixedTimestep.dll
