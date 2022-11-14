@setlocal
@call utils\config.bat || exit

rem  Required argument: the name of the new script. Must be a valid name for
rem  a dir and a C# namespace/region identifier.
set name=%~1

rem  Creates a new script dir `scripts\[name]` based on `scripts\Template`.
rem  Renames the `namespace` and `region` in `Script.cs` to [name].

set scripts_dir=%~dp0..\scripts

set template_dir=%scripts_dir%\Template

if not exist "%template_dir%" (
  echo Script Template is not found: "%template_dir%".
  exit /b 21
)

set src_cs=%template_dir%\%CS%

if not exist "%src_cs%" (
  echo Source is not found: "%src_cs%".
  exit /b 22
)

set dest_dir=%scripts_dir%\%name%

if exist "%dest_dir%" (
  echo The "%name%" already exists.
  exit /b 23
)

mkdir "%dest_dir%" 2>NUL
if %ERRORLEVEL% neq 0 (
  echo Cannot create the "%dest_dir%" dir.
  exit /b 24
)

set dest_cs=%dest_dir%\%CS%
"%SED%" "s/Template/%name%/" "%src_cs%" > "%dest_cs%"

set src_png=%template_dir%\%PNG%
set dest_png=%dest_dir%\%PNG%
copy "%src_png%" "%dest_png%" 1>NUL

echo The "%name%" skeleton has been created.
endlocal
