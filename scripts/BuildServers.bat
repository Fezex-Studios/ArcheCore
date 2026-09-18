@echo off
pushd ..\src\ArcheCore.Server.Auth
bun build src/index.ts --compile --outfile ./bin/ArcheCore.Authserver
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

