@call utils\config.bat || exit

rem  Use `create.bat [name]` to create `scripts\[name]` from `scripts\Template`.
rem  Changes the namespace and region to [name].

set template_dir=scripts\Template
if not exist %template_dir% (
    echo Script template is not found in "%template_dir%".
    exit /b 21
)

set src_cs=%template_dir%\%CS%
if not exist "%src_cs%" (
    echo Source is not found: "%src_cs%"
    exit /b 22
)

set name=%~1
set dest_dir=scripts\%name%
if exist "%dest_dir%" (
    echo Script "%name%" already exists.
    exit /b 23
)

mkdir "%dest_dir%" 2>NUL
if %ERRORLEVEL% neq 0 (
    echo Can not create "%dest_dir%" dir.
    exit /b 24
)

set dest_cs=%dest_dir%\%CS%

"%SED%" "s/Template/%name%/" "%src_cs%" > "%dest_cs%"

set src_png=%template_dir%\%PNG%
set dest_png=%dest_dir%\%PNG%
copy "%src_png%" "%dest_png%" 1>NUL

echo The "%name%" skeleton has been created.
exit /b 0
