@ECHO off
REM Use `export.bat [full path to script_dir]` to export into the game.

REM Settings
SET SED=<Path to Git>\usr\bin\sed.exe
SET SE_DIR=C:\Users\<User>\AppData\Roaming\SpaceEngineers

SET SE_SCRIPTS_DIR=%SE_DIR%\IngameScripts\local
SET REGION="/^\s*#region Ingame/="
SET ENDREGION="/^\s*#endregion \/\/ Ingame/="
SET CS=Script.cs
SET PNG=thumb.png


REM Unquoting the parameter, because the path may be passed both with quotes 
REM (if it contains spaces) and without.
SET script_dir=%~1

IF ["%script_dir%"] == [""] (
    ECHO Script dir is not specified.
    EXIT /B 1
)

IF NOT EXIST %SE_DIR% (
    ECHO Space Engineers AppData is not found: %SE_DIR%
    EXIT /B 2
)

SET src_png=%script_dir%\%PNG%
SET src_cs=%script_dir%\%CS%
IF NOT EXIST "%src_cs%" (
    ECHO Script is not found: %src_cs%
    EXIT /B 3
)

REM Extract `script_dirname` from `script_dir` full path.
FOR %%f IN ("%script_dir%") DO SET script_dirname=%%~nxf

SET dest_dir=%SE_SCRIPTS_DIR%\%script_dirname%
IF NOT EXIST "%dest_dir%" (
    MKDIR "%dest_dir%" 2>NUL
)
IF %ERRORLEVEL% NEQ 0 (
    ECHO Can not resolve path %dest_dir%
    EXIT /B 4
)

REM It will be used to store the line numbers and then the raw ingame script.
SET tmp=%dest_dir%\tmp

REM Find and save the bounds of `#region Ingame`.
%SED% -n %REGION% "%src_cs%" > "%tmp%"
SET /P start_line_num= < "%tmp%"
SET /A start_line_num=start_line_num+1
%SED% -n %ENDREGION% "%src_cs%" > "%tmp%"
SET /P end_line_num= < "%tmp%"
SET /A end_line_num=end_line_num-1

REM Copy the raw ingame script part.
%SED% -n "%start_line_num%,%end_line_num%p" "%src_cs%" > "%tmp%"
IF %ERRORLEVEL% NEQ 0 (
    ECHO Can not extract ingame script part.
    DEL "%tmp%"
    DEL "%dest_dir%\%CS%" 2>NUL
    EXIT /B 5
)

REM Remove first indent (tab or 4 spaces) at the start of each line.
REM Save the script into the final file.
SET TAB_STOP=4
%SED% "s/^\(\s\{%TAB_STOP%\}\|\t\)//" "%tmp%" > "%dest_dir%\%CS%"

DEL "%tmp%"

REM Copy the PNG image.
IF EXIST "%src_png%" (
    COPY "%src_png%" "%dest_dir%\%PNG%" 1>NUL
)

ECHO "%script_dir%" has been exported.
EXIT /B 0
