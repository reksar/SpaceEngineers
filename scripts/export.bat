@ECHO off
REM The `export.bat <script_dir>` cmd exports it to the Space Engineers game.

REM Settings
SET SED=<Path to Git>\usr\bin\sed.exe
SET SRC_DIR=<Path to current repo>\SpaceEngineers\scripts
SET SE_DIR=C:\Users\<User>\AppData\Roaming\SpaceEngineers

SET SE_SCRIPTS_DIR=%SE_DIR%\IngameScripts\local
SET START_MARKER_PATTERN="/^\s*#region Ingame/="
SET END_MARKER_PATTERN="/^\s*#endregion \/\/ Ingame/="
SET CS=Script.cs
SET PNG=thumb.png


REM Sanitizes the quoted <script_dir> argument.
SET script_dir=%~1

IF [%script_dir%] == [] (
    ECHO Script dir is not specified.
    EXIT /B 1
)

IF NOT EXIST %SE_DIR% (
    ECHO Space Engineers AppData is not found: %SE_DIR%
    EXIT /B 2
)

SET src_png=%SRC_DIR%\%script_dir%\%PNG%
SET src_cs=%SRC_DIR%\%script_dir%\%CS%
IF NOT EXIST %src_cs% (
    ECHO Script is not found: %src_cs%
    EXIT /B 3
)

SET dest_dir=%SE_SCRIPTS_DIR%\%script_dir%
IF NOT EXIST %dest_dir% (
    MKDIR %dest_dir%
)

REM First, it will be used to store SED's out, then - to store the raw script.
SET tmp_file=%dest_dir%\tmp

REM Find and save the bounds of ingame script region.
%SED% -n %START_MARKER_PATTERN% %src_cs% > %tmp_file%
SET /P start_line_num= < %tmp_file%
SET /A start_line_num=start_line_num+1
%SED% -n %END_MARKER_PATTERN% %src_cs% > %tmp_file%
SET /P end_line_num= < %tmp_file%
SET /A end_line_num=end_line_num-1

REM Copy the ingame script part into the tmp file.
%SED% -n "%start_line_num%,%end_line_num%p" %src_cs% > %tmp_file%
IF %ERRORLEVEL% NEQ 0 (
    ECHO Can not extract the ingame script part.
    DEL %tmp_file%
    DEL %dest_dir%\%CS% 2>NUL
    EXIT /B 4
)

REM Remove first indent (tab or 4 spaces) at the start of each line.
REM Save script into original file.
SET TAB_STOP=4
%SED% "s/^\(\s\{%TAB_STOP%\}\|\t\)//" %tmp_file% > %dest_dir%\%CS%
DEL %tmp_file%

REM Copy the PNG image.
IF EXIST %src_png% (
    COPY %src_png% %dest_dir%\%PNG% 1>NUL
)

ECHO "%script_dir%" script has been exported.
EXIT /B 0
