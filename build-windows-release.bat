@echo off
setlocal
pushd "%~dp0" || exit /b 1

dotnet publish protoc-gen-turbolink.csproj -c Release -r win-x64 --self-contained true -o publish/windows
if errorlevel 1 goto :failed

@REM copy /Y "publish\windows\protoc-gen-turbolink.exe" "..\..\Plugins\TongosGrpc\GrpcLibraries\Win64\"
@REM if errorlevel 1 goto :failed

popd
exit /b 0

:failed
set "BuildExitCode=%errorlevel%"
popd
exit /b %BuildExitCode%
