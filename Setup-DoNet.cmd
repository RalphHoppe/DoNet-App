@echo off
rem
rem DoNet setup - the one double-click the recipient makes.
rem
rem Keep this file in the folder the packaging wizard produced (the one with
rem Add-AppDevPackage.ps1, the .msix and the .cer), then zip that folder and
rem hand it over. The script trusts the certificate and installs the app,
rem asking for the administrator consent Windows requires for both.
rem
title DoNet setup
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Add-AppDevPackage.ps1"
echo.
echo Finished. DoNet is on the Start menu.
pause
