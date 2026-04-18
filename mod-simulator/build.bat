@echo off
echo === Building IGTAPSimDataExtractor ===
dotnet build -c Release "%~dp0SimDataExtractor.csproj"
