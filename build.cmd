@echo off
setlocal
set "EXPERIMENT_CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%EXPERIMENT_CSC%" set "EXPERIMENT_CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%EXPERIMENT_CSC%" (
  echo .NET Framework C# compiler was not found.
  exit /b 1
)
"%EXPERIMENT_CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /define:PARTICIPANT_BUILD /win32manifest:"%~dp0source\app.manifest" /out:"%~dp0ExperimentUI_Participant.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Configuration.dll "%~dp0source\Program.cs" "%~dp0source\Core.cs" "%~dp0source\SessionStore.cs" "%~dp0source\BreakSchedule.cs"
if errorlevel 1 exit /b 1
if not exist "%~dp0ExperimentUI_Participant.exe.config" (
  copy "%~dp0source\default.config" "%~dp0ExperimentUI_Participant.exe.config" >nul
  if errorlevel 1 exit /b 1
)
echo Built ExperimentUI_Participant.exe
"%EXPERIMENT_CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /define:PARTICIPANT_BUILD,TEST_BUILD /win32manifest:"%~dp0source\app.manifest" /out:"%~dp0ExperimentUI_Test.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Configuration.dll "%~dp0source\Program.cs" "%~dp0source\Core.cs" "%~dp0source\SessionStore.cs" "%~dp0source\BreakSchedule.cs"
if errorlevel 1 exit /b 1
if not exist "%~dp0ExperimentUI_Test.exe.config" (
  copy "%~dp0source\default_test.config" "%~dp0ExperimentUI_Test.exe.config" >nul
  if errorlevel 1 exit /b 1
)
echo Built ExperimentUI_Test.exe
exit /b 0
