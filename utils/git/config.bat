@CALL ..\config.bat || EXIT

SET git=%GIT_DIR%\bin\git.exe --git-dir=%cd%\..\..\.git

%git% config --local filter.unset-git-dir.clean utils/git/unset-git-dir.sh
%git% config --local filter.reset-se-path.clean utils/git/reset-se-path.sh
