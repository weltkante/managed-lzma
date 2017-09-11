@echo off

rem available starting with VS 2017 SP 2 (15.2)
set vswhere="%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"

if not exist %vswhere% (
  echo Could not locate VS 2017
  pause
  exit
)

for /f "usebackq tokens=1* delims=: " %%i in (`%vswhere% -latest -requires Microsoft.Component.MSBuild`) do (
  if /i "%%i"=="installationPath" set InstallDir=%%j
)

set msbuild="%InstallDir%\MSBuild\15.0\Bin\MSBuild.exe"

if not exist %msbuild% (
  echo Could not locate MSBuild
  pause
  exit
)

echo.
echo Building .NET 4.6 library
cd library
%msbuild% /verbosity:minimal /clp:ErrorsOnly /p:Configuration=Release library.csproj
cd ..

echo.
echo Building .NET 4.5 library
cd library45
%msbuild% /verbosity:minimal /clp:ErrorsOnly /p:Configuration=Release library45.csproj
cd ..

echo.
echo Building .NET Standard 1.3 library
cd standard13
%msbuild% /verbosity:minimal /clp:ErrorsOnly /p:Configuration=Release standard13.csproj
cd ..

echo.
echo Building .NET Standard 2.0 library
cd standard20
%msbuild% /verbosity:minimal /clp:ErrorsOnly /p:Configuration=Release standard20.csproj
cd ..

echo.
echo Building nuget package
nuget pack nuget-library.nuspec

pause
