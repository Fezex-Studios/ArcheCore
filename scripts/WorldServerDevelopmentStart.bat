@echo off
pushd ..\src\ArcheCore.Server.World\
$env:DOTNET_ENVIRONMENT = "Development"
dotnet run
popd
pause




