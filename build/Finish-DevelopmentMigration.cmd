@echo off
start "" "%SystemRoot%\System32\conhost.exe" "C:\Program Files\PowerShell\7\pwsh.exe" -NoLogo -NoProfile -NoExit -ExecutionPolicy Bypass -File "%~dp0Finish-DevelopmentMigration.ps1" -Apply
