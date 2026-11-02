@echo off
setlocal
set "EXPERIMENT_CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%EXPERIMENT_CSC%" set "EXPERIMENT_CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%~dp0test_artifacts" mkdir "%~dp0test_artifacts"
"%EXPERIMENT_CSC%" /nologo /target:exe /out:"%~dp0test_artifacts\CoreTests.exe" /reference:System.Drawing.dll "%~dp0source\Core.cs" "%~dp0source\CoreTests.cs"
if errorlevel 1 exit /b 1
"%~dp0test_artifacts\CoreTests.exe"
if errorlevel 1 exit /b 1
"%EXPERIMENT_CSC%" /nologo /target:exe /out:"%~dp0test_artifacts\SessionStoreTests.exe" /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "%~dp0source\Core.cs" "%~dp0source\SessionStore.cs" "%~dp0source\SessionStoreTests.cs" "%~dp0source\BreakSchedule.cs"
if errorlevel 1 exit /b 1
"%~dp0test_artifacts\SessionStoreTests.exe"
if errorlevel 1 exit /b 1
echo All data and storage tests passed.
"%EXPERIMENT_CSC%" /nologo /target:exe /out:"%~dp0test_artifacts\BreakScheduleTests.exe" "%~dp0source\BreakSchedule.cs" "%~dp0source\BreakScheduleTests.cs"
if errorlevel 1 exit /b 1
"%~dp0test_artifacts\BreakScheduleTests.exe"
if errorlevel 1 exit /b 1
echo To run the full-screen GUI check with the supplied 8 trials: ExperimentUI_Participant.exe --smoke
echo To check the self-paced break screen: ExperimentUI_Test.exe --smoke
exit /b 0
