# ============================================================================
#  Mesure le niveau reel de chaque station (ITU-R BS.1770)
# ----------------------------------------------------------------------------
#  A relancer quand on ajoute une station : la valeur Gain= a ecrire dans le
#  .ini vaut  -14 - (LUFS mesure).
#
#      .\outils\mesurer.ps1                 niveau brut des flux
#      .\outils\mesurer.ps1 -Apres          a travers la chaine corrigee du mod
#                                           (gain de station puis limiteur)
#
#  Une minute ne suffit pas pour une station qui alterne animation et musique :
#  pour celles-la, allonger -Secondes, ou relancer et faire la moyenne.
# ============================================================================
param([int]$Secondes = 60, [int]$Ignorer = 10, [switch]$Apres)

$ErrorActionPreference = 'Stop'
$W   = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $PSScriptRoot 'MesureVolume.exe'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

Copy-Item (Join-Path $W 'lib\NAudio.dll') (Join-Path $PSScriptRoot 'NAudio.dll') -Force
if (-not (Test-Path $exe) -or (Get-Item (Join-Path $PSScriptRoot 'MesureVolume.cs')).LastWriteTime -gt (Get-Item $exe).LastWriteTime) {
    & $csc /nologo /optimize+ /target:exe /platform:x64 "/out:$exe" "/reference:$(Join-Path $PSScriptRoot 'NAudio.dll')" (Join-Path $PSScriptRoot 'MesureVolume.cs')
}

$args2 = @((Join-Path $W 'config\WorldRadio.ini'), $Ignorer, $Secondes)
if ($Apres) { $args2 += (Join-Path $W 'WorldRadio.dll') }

Write-Host ("Ecoute de toutes les stations en parallele, {0} s chacune..." -f ($Ignorer + $Secondes)) -ForegroundColor Cyan
'  {0,-3} {1,-18} {2,8} {3,8}  {4}' -f 'N', 'Station', 'LUFS', 'crete', 'Gain conseille'
& $exe @args2 | ForEach-Object {
    $c = $_ -split '\|'
    $conseil = ''
    if ($c[2] -and -not $Apres) {
        $v = [double]::Parse($c[2], [Globalization.CultureInfo]::InvariantCulture)
        $conseil = 'Gain=' + ([Math]::Round(-14.0 - $v, 1)).ToString('0.0', [Globalization.CultureInfo]::InvariantCulture)
    }
    '  {0,-3} {1,-18} {2,8} {3,8}  {4}   {5}' -f $c[0], $c[1], $c[2], $c[4], $conseil, $c[8]
}
