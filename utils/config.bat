@echo off

set "SE_DIR=%userprofile%\AppData\Roaming\SpaceEngineers"
set "SE_SCRIPTS_DIR=%SE_DIR%\IngameScripts\local"
set CS=Script.cs
set PNG=thumb.png

set git_dir=

if exist "%git_path%" (
  rem  Use `git.path` from `settings.json`, which is set as the %git_path% env
  rem  var in `tasks.json`.
  for %%i in ("%git_path%") do (
    set "git_dir=%%~dpi.."
  )
) else (
  rem  Trying to use a system Git path.
  for /f "usebackq delims=" %%i in (`where git 2^>NUL`) do (
    set "git_dir=%%~dpi.."
  )
)

if "%git_dir%" == "" (
  echo Git not found.
  exit /b 1
)

set "sed=%git_dir%\usr\bin\sed.exe"

if not exist "%sed%" (
  echo Not found: "%sed%".
  exit /b 2
)

if not exist "%SE_DIR%" (
  echo Space Engineers AppData not found: "%SE_DIR%".
  exit /b 3
)
