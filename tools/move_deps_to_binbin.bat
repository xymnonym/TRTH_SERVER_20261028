@echo off
setlocal EnableExtensions
rem Keep runtime dependencies beside the server executables.
set "target=%~1"
if not defined target exit /b 2
if not exist "%target%" exit /b 2
for %%F in ("%~dp0..\packages\*.dll") do (
  if not exist "%target%\%%~nxF" (
    copy /y "%%~fF" "%target%\" >nul || exit /b 1
  )
)
exit /b 0