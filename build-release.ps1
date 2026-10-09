$ErrorActionPreference = "Stop"
dotnet restore ".\ElectrosprayControlSystem.sln"
dotnet build ".\ElectrosprayControlSystem.sln" -c Release -p:Platform=x64
Write-Host "Release build completed."
