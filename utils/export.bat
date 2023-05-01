@echo off
setlocal

rem  --------------------------------------------------------------------------
rem  Exports ingame script to Space Engineers local storage:
rem
rem    export {script_dir}
rem
rem  {script_dir} must be a full path to a dir containing the Script.cs and
rem  (optionally) thumb.png file.
rem
rem  You can pass the "${fileWorkspaceFolder}\\${relativeFileDirname}" argument
rem  inside the VS Code task, when a Script.cs file is in the active editor.
rem  --------------------------------------------------------------------------

set utils=%~dp0
call "%utils%config.bat" || exit

set script_dir=%~1

if "%script_dir%" == "" (
  echo Source script dir is not specified.
  exit /b 11
)

set "src_png=%script_dir%\%PNG%"
set "src_cs=%script_dir%\%CS%"

if not exist "%src_cs%" (
  echo Source is not found: "%src_cs%".
  exit /b 12
)

rem  Extract `src_dirname` from the `script_dir` full path.
for %%i in ("%script_dir%") do (
  set src_dirname=%%~nxi
)

set "dest_dir=%SE_SCRIPTS_DIR%\%src_dirname%"

rem  Create destination dir in the game local storage.
if not exist "%dest_dir%" (
  md "%dest_dir%" 2>NUL
)
if %ERRORLEVEL% NEQ 0 (
  echo Cannot create "%dest_dir%" dir.
  exit /b 13
)

rem  Find the C# namespace.
for /f "tokens=2" %%i in ('findstr /i /b "namespace" "%src_cs%"') do (
  set namespace=%%i
)

if "%namespace%" == "" (
  call "%utils%export-entire" || exit
) else (
  call "%utils%export-namespace" || exit
)

if exist "%src_png%" (
  copy "%src_png%" "%dest_dir%\%PNG%" 1>NUL
)

echo "%script_dir%" has been exported.
endlocal
