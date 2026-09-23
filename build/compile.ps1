$ErrorActionPreference = 'Stop'
# racine du depot, deduite de l'emplacement de ce script
$W    = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'trouve-jeu.ps1')
$game = Trouve-Jeu
$csc  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$src  = Join-Path $W 'src'
$out  = Join-Path $W 'WorldRadio.dll'
$naud = Join-Path $W 'lib\NAudio.dll'

$sources = @(
    (Join-Path $src 'Journal.cs'),
    (Join-Path $src 'Systeme.cs'),
    (Join-Path $src 'Natif.cs'),
    (Join-Path $src 'Config.cs'),
    (Join-Path $src 'Lecteur.cs'),
    (Join-Path $src 'Metadonnees.cs'),
    (Join-Path $src 'Pause.cs'),
    (Join-Path $src 'Audio.cs'),
    (Join-Path $src 'Menu.cs'),
    (Join-Path $src 'Ecran.cs'),
    (Join-Path $src 'Dessin.cs'),
    (Join-Path $src 'Spectre.cs'),
    (Join-Path $src 'Limiteur.cs'),
    (Join-Path $src 'Selecteur.cs'),
    (Join-Path $src 'Apercu.cs'),
    (Join-Path $src 'WorldRadio.cs')
)
foreach ($s in $sources) { if (-not (Test-Path -LiteralPath $s)) { throw "source manquante : $s" } }
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Force }

$args = @(
  '/target:library', "/out:$out", '/nologo', '/optimize+', '/warnaserror-',
  "/reference:$(Join-Path $game 'ScriptHookVDotNet3.dll')",
  "/reference:$naud",
  '/reference:System.dll', '/reference:System.Core.dll',
  '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll'
) + $sources

Write-Output ('=== compilation : ' + $sources.Count + ' fichiers ===')
& $csc $args 2>&1 | ForEach-Object { '  ' + $_ }
if (-not (Test-Path -LiteralPath $out)) { throw 'COMPILATION ECHOUEE' }
Write-Output ('  OK : {0:N0} octets' -f (Get-Item -LiteralPath $out).Length)
