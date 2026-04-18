@echo off
echo === Building IGTAPMinimap ===
dotnet build -c Release "%~dp0Minimap.csproj"
