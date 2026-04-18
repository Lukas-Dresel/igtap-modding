@echo off
echo === Building IGTAPCheckpoint ===
dotnet build -c Release "%~dp0Checkpoint.csproj"
