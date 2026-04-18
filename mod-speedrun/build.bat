@echo off
echo === Building IGTAPSpeedrun ===
dotnet build -c Release "%~dp0SpeedrunTimer.csproj"
