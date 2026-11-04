@echo off
setlocal
cd /d "%~dp0"
set NAME=LuckyDice
set DLL=bin\Release\net6.0\%NAME%.dll
if not exist "%DLL%" (
  echo %DLL% not found - run build.bat first.
  pause
  exit /b
)
if exist pkg rmdir /s /q pkg
mkdir "pkg\plugins\%NAME%"
copy /y thunderstore\manifest.json pkg\ >nul
copy /y thunderstore\README.md pkg\ >nul
copy /y thunderstore\CHANGELOG.md pkg\ >nul
copy /y thunderstore\icon.png pkg\ >nul
copy /y "%DLL%" "pkg\plugins\%NAME%\" >nul
copy /y "Assets\dice.mesh" "pkg\plugins\LuckyDice\" >nul
copy /y "Assets\dice.png" "pkg\plugins\LuckyDice\" >nul
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Get-Content thunderstore\manifest.json | ConvertFrom-Json).version_number"`) do set VER=%%v
set OUT=%NAME%-%VER%.zip
if exist "%OUT%" del "%OUT%"
powershell -NoProfile -Command "Compress-Archive -Path 'pkg\*' -DestinationPath '%OUT%'"
rmdir /s /q pkg
echo.
echo Created %OUT% - upload it at https://thunderstore.io/package/create/
pause
