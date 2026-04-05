@echo off
echo === Building IGTAPInputViz ===
dotnet build -c Release "%~dp0InputViz.csproj"
