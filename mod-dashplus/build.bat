@echo off
echo === Building IGTAPDashPlus ===
dotnet build -c Release "%~dp0DashPlus.csproj"
