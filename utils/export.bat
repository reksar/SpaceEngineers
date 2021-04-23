@CALL utils\config.bat || EXIT

REM Exports ingame region of a script to the Space Engineers local storage.
REM Use `export.bat [full path to src_dir]` manually
REM or `export.bat "${fileWorkspaceFolder}\\${relativeFileDirname}"` from 
REM VS Code task, when a `Script.cs` file is in the active editor.

REM Unquoting the parameter, because the path may be passed both with quotes 
REM (if it contains spaces) and without.
SET src_dir=%~1

IF ["%src_dir%"] == [""] (
    ECHO Source script dir is not specified.
    EXIT /B 11
)

SET src_png=%src_dir%\%PNG%
SET src_cs=%src_dir%\%CS%
IF NOT EXIST "%src_cs%" (
    ECHO Source is not found: "%src_cs%"
    EXIT /B 12
)

REM Find a namespace in the given source.
FOR /F "tokens=2" %%G IN ('FINDSTR /I /B "namespace" "%src_cs%"') DO (
    SET namespace=%%G
)
IF [%namespace%] == [] (
    ECHO The namespace is not found in "%src_cs%"
    EXIT /B 13
)

REM Extract `src_dirname` from `src_dir` full path.
FOR %%G IN ("%src_dir%") DO (
    SET src_dirname=%%~nxG
)
IF NOT ["%src_dirname%"] == ["%namespace%"] (
    ECHO WARN: "%src_dirname%" dir and %namespace% namespace is not same!
)

REM Create a dir in the game local storage.
SET dest_dir=%SE_SCRIPTS_DIR%\%src_dirname%
IF NOT EXIST "%dest_dir%" (
    MKDIR "%dest_dir%" 2>NUL
)
IF %ERRORLEVEL% NEQ 0 (
    ECHO Can not create "%dest_dir%" dir.
    EXIT /B 14
)

REM It will be used to store the line numbers and then the raw ingame script.
SET tmp=%dest_dir%\tmp

REM Set ingame region patterns based on namespace.
SET region="/^\s*#region %namespace%/="
SET endregion="/^\s*#endregion \/\/ %namespace%/="
REM Find and save the bounds of the ingame region.
%SED% -n %region% "%src_cs%" > "%tmp%"
SET /P start_line_num= < "%tmp%"
%SED% -n %endregion% "%src_cs%" > "%tmp%"
SET /P end_line_num= < "%tmp%"

REM Copy the raw ingame script part.
%SED% -n "%start_line_num%,%end_line_num%p" "%src_cs%" > "%tmp%"
IF %ERRORLEVEL% NEQ 0 (
    ECHO Can not extract ingame script part.
    DEL "%tmp%"
    DEL "%dest_dir%\%CS%" 2>NUL
    EXIT /B 15
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

ECHO "%src_dir%" has been exported.
EXIT /B 0
