@ECHO off

SET GIT_DIR=
SET SED=%GIT_DIR%\usr\bin\sed.exe
SET SE_DIR=%userprofile%\AppData\Roaming\SpaceEngineers
SET SE_SCRIPTS_DIR=%SE_DIR%\IngameScripts\local
SET CS=Script.cs
SET PNG=thumb.png

IF NOT EXIST "%GIT_DIR%" (
    ECHO Git is not found in "%GIT_DIR%".
    EXIT /B 1
)

IF NOT EXIST "%SED%" (
    ECHO Sed editor is not found in "%SED%".
    EXIT /B 2
)

IF NOT EXIST "%SE_DIR%" (
    ECHO Space Engineers AppData is not found in "%SE_DIR%".
    EXIT /B 3
)
