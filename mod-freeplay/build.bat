@echo off
echo === Building IGTAPFreeplay ===
dotnet build -c Release "%~dp0Freeplay.csproj"
