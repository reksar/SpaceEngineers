@setlocal
@call utils\config.bat || exit

rem  Exports ingame script to Space Engineers local storage.

rem  Required argument: the full path to a dir containing the Script.cs and
rem  (optionally) thumb.png file.
rem
rem  You can pass the "${fileWorkspaceFolder}\\${relativeFileDirname}" argument
rem  inside the VS Code task, when a Script.cs file is in the active editor.
set script_dir=%~1

if "%script_dir%"=="" (
  echo Source script dir is not specified.
  exit /b 11
)

set src_png=%script_dir%\%PNG%
set src_cs=%script_dir%\%CS%

if not exist "%src_cs%" (
  echo Source is not found: "%src_cs%".
  exit /b 12
)

rem  Find the namespace in the given source.
for /f "tokens=2" %%i in ('findstr /i /b "namespace" "%src_cs%"') do (
  set namespace=%%i
)

if "%namespace%"=="" (
  echo The namespace is not found in "%src_cs%".
  exit /b 13
)

rem  Extract `src_dirname` from the `script_dir` full path.
for %%i in ("%script_dir%") do (
  set src_dirname=%%~nxi
)

if not "%src_dirname%"=="%namespace%" (
  echo WARN: "%src_dirname%" dir and %namespace% namespace is not same!
)

set dest_dir=%SE_SCRIPTS_DIR%\%src_dirname%

rem  Create a dir in the game local storage.
if not exist "%dest_dir%" (
  mkdir "%dest_dir%" 2>NUL
)
if %ERRORLEVEL% neq 0 (
  echo Cannot create "%dest_dir%" dir.
  exit /b 14
)

rem  It will be used to store the line numbers and then the raw ingame script.
set tmp=%dest_dir%\tmp

rem  Set ingame region patterns based on namespace.
set region="/^\s*#region %namespace%/="
set endregion="/^\s*#endregion \/\/ %namespace%/="

rem  Find and save the bounds of the ingame region.
"%SED%" -n %region% "%src_cs%" > "%tmp%"
set /p start_line_num= < "%tmp%"
"%SED%" -n %endregion% "%src_cs%" > "%tmp%"
set /p end_line_num= < "%tmp%"

rem  Copy the raw ingame script part.
"%SED%" -n "%start_line_num%,%end_line_num%p" "%src_cs%" > "%tmp%"
if %ERRORLEVEL% neq 0 (
  echo Can not extract ingame script part.
  del "%tmp%"
  del "%dest_dir%\%CS%" 2>NUL
  exit /b 15
)

rem  Remove the first indent (tab or 4 spaces) at the start of each line and
rem  save the script into the final file.
set TAB_STOP=4
"%SED%" "s/^\(\s\{%TAB_STOP%\}\|\t\)//" "%tmp%" > "%dest_dir%\%CS%"

del "%tmp%"

rem  Copy the PNG image.
if exist "%src_png%" (
  copy "%src_png%" "%dest_dir%\%PNG%" 1>NUL
)

echo "%script_dir%" has been exported.
endlocal
