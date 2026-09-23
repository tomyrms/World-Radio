@echo off
setlocal
cd /d "%~dp0"
title World Radio - installation

if not exist "%~dp0fichiers\installe.ps1" (
    echo.
    echo   Le dossier "fichiers" est introuvable a cote de ce fichier.
    echo.
    echo   Decompresse d'abord TOUT le zip ^(clic droit ^> Extraire tout^),
    echo   puis lance INSTALLER.cmd depuis le dossier extrait.
    echo.
    pause
    exit /b 1
)

powershell -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0fichiers\installe.ps1"
