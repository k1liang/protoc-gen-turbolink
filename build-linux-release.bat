@echo off
setlocal
pushd "%~dp0" || exit /b 1

dotnet publish protoc-gen-turbolink.csproj -c Release -r linux-x64 --self-contained true -o publish/linux
if errorlevel 1 goto :failed

@REM copy /Y "publish\linux\protoc-gen-turbolink" "..\..\Plugins\TongosGrpc\GrpcLibraries\Linux\"
@REM if errorlevel 1 goto :failed

popd
exit /b 0

:failed
set "BuildExitCode=%errorlevel%"
popd
exit /b %BuildExitCode%
