@echo off
echo === Building IGTAPEcho ===
dotnet build -c Release "%~dp0Echo.csproj"
