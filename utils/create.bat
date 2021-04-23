@CALL utils\config.bat || EXIT

REM Use `create.bat [name]` to create `scripts\[name]` from `scripts\Template`.
REM Changes the namespace and region to [name].

SET TEMPLATE_DIR=scripts\Template
IF NOT EXIST %TEMPLATE_DIR% (
    ECHO Script template is not found in "%TEMPLATE_DIR%".
    EXIT /B 21
)

SET src_cs=%TEMPLATE_DIR%\%CS%
IF NOT EXIST "%src_cs%" (
    ECHO Source is not found: "%src_cs%"
    EXIT /B 22
)

SET name=%~1
SET dest_dir=scripts\%name%
IF EXIST "%dest_dir%" (
    ECHO Script "%name%" already exists.
    EXIT /B 23
)

MKDIR "%dest_dir%" 2>NUL
IF %ERRORLEVEL% NEQ 0 (
    ECHO Can not create "%dest_dir%" dir.
    EXIT /B 24
)

SET dest_cs=%dest_dir%\%CS%

%SED% "s/Template/%name%/" "%src_cs%" > "%dest_cs%"

SET src_png=%TEMPLATE_DIR%\%PNG%
SET dest_png=%dest_dir%\%PNG%
COPY "%src_png%" "%dest_png%" 1>NUL

ECHO The "%name%" skeleton has been created.
EXIT /B 0
