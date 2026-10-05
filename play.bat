@echo off
setlocal
rem Double-click to compile the C# project and open the game on Windows.
set "ROOT=%~dp0"
set "GODOT=%LOCALAPPDATA%\Godot\Godot_v4.7.2-stable_mono_win64"
set "PLAYER=%GODOT%\Godot_v4.7.2-stable_mono_win64.exe"
set "NUPKGS=%GODOT%\GodotSharp\Tools\nupkgs"
set "BUILD=%LOCALAPPDATA%\DigimonWorldEternity\bin\Debug"
set "OBJ=%LOCALAPPDATA%\DigimonWorldEternity\obj"
set "GAMEBIN=%ROOT%src\.godot\mono\temp\bin\Debug"

if not exist "%PLAYER%" (
  echo Godot 4.7.2 .NET was not found:
  echo   %PLAYER%
  echo Install the Godot 4.7 .NET editor for Windows, then run this again.
  pause
  exit /b 1
)

where dotnet >nul 2>&1
if errorlevel 1 (
  echo The .NET 8 SDK was not found. Install it, then run this again.
  pause
  exit /b 1
)

echo Closing a previous launch...
taskkill /F /IM Godot_v4.7.2-stable_mono_win64.exe >nul 2>&1
taskkill /F /IM Godot_v4.7.2-stable_mono_win64_console.exe >nul 2>&1

rem Compile outside OneDrive. The project folder rejects the compiler's own file copy.
echo Building Digimon World Eternity...
dotnet build "%ROOT%src\DigimonWorldEternity.csproj" -c Debug --source "%NUPKGS%" -p:OutputPath="%BUILD%\\" -p:BaseOutputPath="%LOCALAPPDATA%\DigimonWorldEternity\bin\\" -p:BaseIntermediateOutputPath="%OBJ%\\" -p:IntermediateOutputPath="%OBJ%\Debug\\" -p:MSBuildProjectExtensionsPath="%OBJ%\\"
if errorlevel 1 (
  echo.
  echo Build failed.
  pause
  exit /b 1
)

if not exist "%GAMEBIN%" mkdir "%GAMEBIN%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Copy-Item -Force -Path '%BUILD%\*' -Destination '%GAMEBIN%'"
if errorlevel 1 (
  echo.
  echo The game was built, but its files could not be updated. Close any open copy and run this again.
  pause
  exit /b 1
)

echo Launching...
start "" "%PLAYER%" --path "%ROOT%src"
endlocal
