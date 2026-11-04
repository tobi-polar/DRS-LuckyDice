@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"

where dotnet >nul 2>nul
if not errorlevel 1 goto havedotnet
echo .NET SDK not found - installing it with winget...
winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements
echo.
echo Close this window and double-click build.bat again.
pause
exit /b

:havedotnet
set "GAMEDIR=C:\Program Files (x86)\Steam\steamapps\common\Deep Rock Galactic Survivor"

:checkgame
if exist "!GAMEDIR!\BepInEx\interop\Assembly-CSharp.dll" goto build
echo.
echo Couldn't find BepInEx\interop\Assembly-CSharp.dll in:
echo   "!GAMEDIR!"
echo (Make sure BepInEx is installed and you've launched the game once.)
set /p "GAMEDIR=Paste your Deep Rock Galactic Survivor folder path: "
set "GAMEDIR=!GAMEDIR:"=!"
goto checkgame

:build
dotnet build -c Release "/p:GameDir=!GAMEDIR!"
if errorlevel 1 goto failed
echo.
echo Done. LuckyDice.dll is in "!GAMEDIR!\BepInEx\plugins\LuckyDice"
pause
exit /b

:failed
echo.
echo Build failed - copy the errors above and send them to Claude.
pause
