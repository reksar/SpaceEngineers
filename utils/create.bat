@CALL utils\config.bat || EXIT

REM Use `create.bat [name]` to create `scripts\[name]` from `scripts\Template`.
REM Changes the namespace and region name to [name].
REM Removes all multiline comments in `Script.cs`.

SET src_cs=%TEMPLATE_DIR%\%CS%
IF NOT EXIST "%src_cs%" (
    ECHO Source is not found: "%src_cs%"
    EXIT /B 21
)

SET name=%~1
SET dest_dir=scripts\%name%
IF EXIST "%dest_dir%" (
    ECHO Script "%name%" already exists.
    EXIT /B 22
)

MKDIR "%dest_dir%" 2>NUL
IF %ERRORLEVEL% NEQ 0 (
    ECHO Can not create "%dest_dir%" dir.
    EXIT /B 23
)

SET dest_cs=%dest_dir%\%CS%

%SED% -f %cd%\utils\remove-multiline-comments.sed "%src_cs%" > "%dest_cs%"

SET src_png=%TEMPLATE_DIR%\%PNG%
SET dest_png=%dest_dir%\%PNG%
COPY "%src_png%" "%dest_png%" 1>NUL

ECHO The "%name%" skeleton has been created.
EXIT /B 0
