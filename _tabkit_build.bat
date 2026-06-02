@echo off
setlocal
cd /d "%~dp0"
set LOG=_tabkit_build.log
echo TABKIT_BUILD_START %DATE% %TIME%> "%LOG%"
echo ---DOTNET INFO--->> "%LOG%"
where dotnet >> "%LOG%" 2>&1
dotnet --version >> "%LOG%" 2>&1
echo ---CLEAN RELEASE--->> "%LOG%"
dotnet clean Tabkit.slnx -c Release >> "%LOG%" 2>&1
echo ---BUILD RELEASE--->> "%LOG%"
dotnet build Tabkit.slnx -c Release >> "%LOG%" 2>&1
set BERR=%ERRORLEVEL%
echo BUILD_EXIT=%BERR%>> "%LOG%"
set EXE=src\Tabkit.App\bin\Release\net10.0-windows\tabkit-app.exe
if "%BERR%"=="0" (
  echo LAUNCHING %EXE%>> "%LOG%"
  if exist "%EXE%" (
    start "" "%EXE%"
    echo LAUNCHED_OK>> "%LOG%"
  ) else (
    echo EXE_NOT_FOUND_AT_EXPECTED_PATH>> "%LOG%"
  )
) else (
  echo BUILD_FAILED_NO_LAUNCH>> "%LOG%"
)
echo TABKIT_BUILD_DONE %DATE% %TIME%>> "%LOG%"
