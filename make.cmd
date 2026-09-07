@echo off
rem
rem  //\\   OmenMon: Hardware Monitoring & Control Utility
rem //  \\  Copyright © 2023 Piotr Szczepański * License: GPL3
rem     //  https://omenmon.github.io/
rem
set DOTNET_CLI_TELEMETRY_OPTOUT=1
setlocal
rem Search for MSBuild. vswhere is asked first because it knows where each
rem edition actually landed; the fixed paths below are only a fallback. Build
rem Tools installs under Program Files (x86), which the %ProgramFiles%-only
rem search used to miss - it then fell through to a bare "msbuild" and picked up
rem whatever was on PATH, here the .NET Framework one, which cannot compile
rem langversion 11 and fails with CS1617.
set msbuild=
set pf=%ProgramFiles%
set pfx=%ProgramFiles(x86)%
set vswhere="%pfx%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist %vswhere% for /f "usebackq delims=" %%v in (`%vswhere% -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\amd64\MSBuild.exe`) do set msbuild="%%v"
for %%e in (BuildTools Community Professional Enterprise) do for %%r in ("%pf%" "%pfx%") do (
    if not defined msbuild if exist "%%~r\Microsoft Visual Studio\2022\%%e\MSBuild\Current\Bin\amd64\MSBuild.exe" set msbuild="%%~r\Microsoft Visual Studio\2022\%%e\MSBuild\Current\Bin\amd64\MSBuild.exe"
)
if not defined msbuild set msbuild=msbuild
rem Version is not pinned here: OmenMon.csproj defaults AssemblyVersion /
rem AssemblyVersionWord (and CI's build_bump.yml overrides them for tagged
rem releases), so a local `make build` always picks up the current version
rem without this file needing a bump every release.
set msbuild_flags=/p:Configuration=Release
set nuget=%~dps0nuget.exe
set nuget_url=https://dist.nuget.org/win-x86-commandline/latest/nuget.exe
set op_scope=build clean kill prepare test usage
set op=%~1
set taskkill=%SystemRoot%\System32\taskkill.exe
set result_bin=OmenMon.exe
set test_param=-Ec -Ec HPCM RPM0^^(2^^) RPM2^^(2^^) -Bios -Bios Backlight=Off Color=0080FF:00FF00:00FF00:FFFFFF Backlight=On -Prog -Task -Usage
pushd %~dps0
for %%p in (%op_scope%) do if "%op%"=="%%p" echo BEGIN %~n0 (%op%) & goto %op%
echo BEGIN %~n0 & goto usage

:build
call :TaskKill %result_bin%
%msbuild% /t:Clean,Build %msbuild_flags%
goto end

:clean
call :TaskKill %result_bin%
%msbuild% /t:Clean
rem Handled within .csproj now:
rem call :RecursivelyRemoveDir Bin
rem call :RecursivelyRemoveDir Obj
goto end

:kill
call :TaskKill %result_bin%
goto end

:prepare
if not exist %nuget% powershell Invoke-WebRequest -OutFile %nuget% -Uri %nuget_url%
%nuget% restore
goto end

:test
if exist Bin\%result_bin% ( Bin\%result_bin% %test_param% ) ^
else if exist %result_bin% %result_bin% %test_param%
goto end

:usage
echo Usage: %~n0 ^<build^|clean^|kill^|prepare^|test^|usage^>
goto end

:RecursivelyRemoveDir
if not "%1"=="" for /d /r %%d in (*) do if exist %%~dpsd%1 rd /q /s %%~dpsd%1
exit /b

:TaskKill
%taskkill% /f /im "%1"
exit /b

:end
echo END %~n0
popd
