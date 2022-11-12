setlocal

@call %~dp0..\config.bat || exit

set git_local_dir=%~dp0..\..\.git
set git="%GIT_DIR%\cmd\git.exe" --git-dir="%git_local_dir%"

%git% config --local filter.unset-git-dir.clean utils/git/unset-git-dir.sh
%git% config --local filter.reset-se-path.clean utils/git/reset-se-path.sh

endlocal
