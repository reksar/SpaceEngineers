@setlocal
@call %~dp0..\config.bat || exit

git --git-dir="%~dp0..\..\.git" ^
  config --local filter.reset-se-path.clean utils/git/reset-se-path.sh

endlocal
