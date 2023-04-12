@echo off
echo [WARN] C# namespace not found in "%src_cs%". Copying the entire script.
copy "%src_cs%" "%dest_dir%\%CS%" 1>NUL
