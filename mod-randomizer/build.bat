@echo off
echo === Building IGTAPRandomizer ===
dotnet build -c Release "%~dp0Randomizer.csproj"
