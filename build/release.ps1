# ============================================================================
#  Fabrique le zip a distribuer : dist\WorldRadio.zip
# ----------------------------------------------------------------------------
#  Celui qui recoit le zip n'a rien a compiler : le DLL est deja dedans. Il
#  decompresse et double-clique sur INSTALLER.cmd.
#
#      WorldRadio\
#        INSTALLER.cmd              <- le seul fichier a lancer
#        LISEZMOI.txt
#        fichiers\
#          installe.ps1, trouve-jeu.ps1
#          WorldRadio.dll, WorldRadio.ini, NAudio.dll
#          WorldRadio\              24 images
#
#  Les tests rapides passent avant : on ne distribue pas un zip casse.
# ============================================================================

$ErrorActionPreference = 'Stop'
$W = Split-Path -Parent $PSScriptRoot

Write-Host '=== 1. compilation ===' -ForegroundColor Cyan
& (Join-Path $W 'build\compile.ps1')

Write-Host ''
Write-Host '=== 2. tests rapides ===' -ForegroundColor Cyan
foreach ($t in 'niveau', 'roue', 'pieton', 'installation') {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $W "tests\$t.ps1") | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "le test $t echoue : zip non fabrique" }
    Write-Host ('  ' + $t + ' : passe') -ForegroundColor Green
}

Write-Host ''
Write-Host '=== 3. assemblage ===' -ForegroundColor Cyan
$dist = Join-Path $W 'dist'
$pack = Join-Path $dist 'WorldRadio'
if (Test-Path -LiteralPath $pack) { Remove-Item -LiteralPath $pack -Recurse -Force }
$fich = Join-Path $pack 'fichiers'
New-Item -ItemType Directory -Force -Path (Join-Path $fich 'WorldRadio') | Out-Null

Copy-Item (Join-Path $W 'install\INSTALLER.cmd') $pack
Copy-Item (Join-Path $W 'install\LISEZMOI.txt') $pack
Copy-Item (Join-Path $W 'install\installe.ps1') $fich
Copy-Item (Join-Path $W 'install\trouve-jeu.ps1') $fich
Copy-Item (Join-Path $W 'WorldRadio.dll') $fich
Copy-Item (Join-Path $W 'config\WorldRadio.ini') $fich
Copy-Item (Join-Path $W 'lib\NAudio.dll') $fich
Copy-Item (Join-Path $W 'assets\icons\*.png') (Join-Path $fich 'WorldRadio')

$zip = Join-Path $dist 'WorldRadio.zip'
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($pack, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)

$n = (Get-ChildItem -LiteralPath $pack -Recurse -File).Count
Write-Host ('  {0} fichiers, {1:N0} Ko' -f $n, ((Get-Item -LiteralPath $zip).Length / 1KB)) -ForegroundColor Green
Write-Host ('  ' + $zip) -ForegroundColor Green
