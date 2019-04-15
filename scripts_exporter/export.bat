@echo off

REM This script used as an external tool in Visual Studio to export ingame 
REM scripts for Space Engineers game.
REM Params:
REM     %1 - Project name

SET SOURCE_ROOT=..\scripts
SET DESTINATION_ROOT=C:\Users\reksar\AppData\Roaming\SpaceEngineers\IngameScripts\local
SET STRART_LINE_PATTERN="/^\s\+\/\/ INGAME SCRIPT START/="
SET END_LINE_PATTERN="/^\s\+\/\/ INGAME SCRIPT END/="
SET SED=E:\reksar\soft\portable\git\usr\bin\sed.exe
SET FILENAME=Script.cs

SET project_name=%1
IF [%project_name%] == [] (
    ECHO C# project name is not specified.
    EXIT /B 1
)
SET source_file=%SOURCE_ROOT%\%project_name%\%FILENAME%
IF NOT EXIST %source_file% (
    ECHO Source file is not found: %source_file%
    EXIT /B 1
)
IF NOT EXIST %DESTINATION_ROOT% (
    ECHO Space Engineers destination folder is not found: %DESTINATION_ROOT%
    EXIT /B 1
)
SET destination_dir=%DESTINATION_ROOT%\%project_name%
IF NOT EXIST %destination_dir% MKDIR %destination_dir%

REM Find first and last line of ingame script in the source file.
REM Save the numbers of it lines.
SET tmp_file=%destination_dir%\tmp
%SED% -n %STRART_LINE_PATTERN% %source_file% > %tmp_file%
SET /P start_line_num= < %tmp_file%
%SED% -n %END_LINE_PATTERN% %source_file% > %tmp_file%
SET /P end_line_num= < %tmp_file%

REM Copy ingame part of the source file into Space Engineers game dir.
%SED% -n "%start_line_num%,%end_line_num%p" %source_file% > %tmp_file%

REM Remove first 4 spaces or tabulation at start of each line.
SET script=%destination_dir%\%FILENAME%
%SED% "s/^\(\s\{4\}\|\t\)//" %tmp_file% > %script%
DEL %tmp_file%

EXIT /B 0
