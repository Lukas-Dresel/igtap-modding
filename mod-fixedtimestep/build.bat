@echo off
echo === Building IGTAPFixedTimestep ===
dotnet build -c Release "%~dp0FixedTimestep.csproj"
