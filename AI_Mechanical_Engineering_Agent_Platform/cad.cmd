@echo off
rem cad command line: incremental build, then run.  Usage: cad run planetary_gear  (see README)
dotnet build "%~dp0src\CadCli\CadCli.csproj" --nologo -v q -clp:NoSummary >nul || exit /b 1
dotnet "%~dp0src\CadCli\bin\Debug\net10.0-windows\cad.dll" %*
