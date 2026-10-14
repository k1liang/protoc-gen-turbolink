@echo off
setlocal
pushd "%~dp0" || exit /b 1

call "%~dp0build-windows-release.bat"
if errorlevel 1 goto :failed

call "%~dp0build-linux-release.bat"
if errorlevel 1 goto :failed

popd
exit /b 0

:failed
set "BuildExitCode=%errorlevel%"
popd
exit /b %BuildExitCode%
