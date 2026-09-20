@echo off
pushd ..\src\ArcheCore.Server.Auth
dotnet build -c Debug
popd


@echo off
pushd ..\src\ArcheCore.Server.World
dotnet build -c Debug
popd

@echo off
pushd ..\src\ArcheCore.Server.Persistence
dotnet build -c Debug
popd
pause

