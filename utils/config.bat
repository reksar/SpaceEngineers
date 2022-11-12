@echo off

set GIT_DIR=
set SED=%GIT_DIR%\usr\bin\sed.exe
set SE_DIR=%userprofile%\AppData\Roaming\SpaceEngineers
set SE_SCRIPTS_DIR=%SE_DIR%\IngameScripts\local
set CS=Script.cs
set PNG=thumb.png

if not exist "%GIT_DIR%" (
    echo Git is not found in "%GIT_DIR%".
    exit /b 1
)

if not exist "%SED%" (
    echo Sed editor is not found in "%SED%".
    exit /b 2
)

if not exist "%SE_DIR%" (
    echo Space Engineers AppData is not found in "%SE_DIR%".
    exit /b 3
)
