@echo off
echo === Building IGTAPReplay ===
dotnet build -c Release "%~dp0Replay.csproj"
