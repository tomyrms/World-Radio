# ============================================================================
#  Detection du jeu, sur des installations FABRIQUEES
# ----------------------------------------------------------------------------
#  Rien n'est lu de la machine qui lance ce test : registre, processus et
#  disques reels sont exclus. On fabrique a la place les cas qu'on rencontre
#  chez les autres :
#
#    - Epic Games, jeu dans un dossier "GTAV" (le nom ne dit pas "Grand
#      Theft Auto") : c'est ce qui faisait echouer l'ancien installeur
#    - Epic, connu seulement par LauncherInstalled.dat
#    - Steam, bibliotheque sur un autre disque, chemin quelconque
#    - aucun launcher : jeu copie a la main, trouve par la recherche
#    - un reste de desinstallation, sans l'exe : doit etre ecarte
#    - la version Legacy (GTA5.exe) : doit etre ecartee
# ============================================================================

$ErrorActionPreference = 'Stop'
$W = Split-Path -Parent $PSScriptRoot
. (Join-Path $W 'install\trouve-jeu.ps1')

$echecs = 0
function Ok($m)    { Write-Host ('  ok    : ' + $m) -ForegroundColor Green }
function Echec($m) { Write-Host ('  ECHEC : ' + $m) -ForegroundColor Red; $script:echecs++ }

$T = Join-Path ([IO.Path]::GetTempPath()) ('wr_detection_' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force -Path $T | Out-Null

function Jeu([string]$dossier, [string]$exe = 'GTA5_Enhanced.exe') {
    New-Item -ItemType Directory -Force -Path $dossier | Out-Null
    Set-Content -LiteralPath (Join-Path $dossier $exe) -Value 'stub'
    return $dossier
}
function Json($objet, [string]$chemin) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $chemin) | Out-Null
    ($objet | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath $chemin -Encoding UTF8
}
function Chercher([hashtable]$p) {
    $base = @{ SansRegistre = $true; SansProcessus = $true; SansScan = $true; ProgramData = '' }
    foreach ($k in $p.Keys) { $base[$k] = $p[$k] }
    Trouve-Candidats @base
}

Write-Host ''
Write-Host '=== detection du jeu ===' -ForegroundColor Cyan

# --- 1. Epic, dossier GTAV ---------------------------------------------------
$pd1 = Join-Path $T 'pd1'
$jeu1 = Jeu (Join-Path $T 'D\Jeux\Epic Games\GTAV')
Json @{ DisplayName = 'Grand Theft Auto V Enhanced'; InstallLocation = $jeu1; LaunchExecutable = 'PlayGTAV.exe' } `
     (Join-Path $pd1 'Epic\EpicGamesLauncher\Data\Manifests\A1B2C3.item')
Json @{ DisplayName = 'Fortnite'; InstallLocation = (Join-Path $T 'D\Jeux\Fortnite') } `
     (Join-Path $pd1 'Epic\EpicGamesLauncher\Data\Manifests\FFFF.item')
$r = @(Chercher @{ ProgramData = $pd1 })
if ($r.Count -eq 1 -and $r[0].Dossier -ieq $jeu1 -and $r[0].Source -eq 'Epic Games') {
    Ok 'Epic : jeu trouve dans un dossier nomme GTAV (le cas qui echouait)'
} else { Echec ("Epic / GTAV : " + (($r | ForEach-Object { $_.Dossier }) -join ' | ')) }

# --- 2. Epic, par LauncherInstalled.dat seulement ----------------------------
$pd2 = Join-Path $T 'pd2'
$jeu2 = Jeu (Join-Path $T 'E\Autre\GTAVEnhanced')
Json @{ InstallationList = @(@{ InstallLocation = $jeu2; AppName = 'xyz' }) } `
     (Join-Path $pd2 'Epic\UnrealEngineLauncher\LauncherInstalled.dat')
$r = @(Chercher @{ ProgramData = $pd2 })
if ($r.Count -eq 1 -and $r[0].Dossier -ieq $jeu2) { Ok 'Epic : jeu trouve par LauncherInstalled.dat' }
else { Echec 'Epic / LauncherInstalled.dat non exploite' }

# --- 3. Steam, bibliotheque ailleurs ------------------------------------------
$steam = Join-Path $T 'Steam'
$bibli = Join-Path $T 'F\Mes Jeux Steam'
$jeu3 = Jeu (Join-Path $bibli 'steamapps\common\Grand Theft Auto V Enhanced')
New-Item -ItemType Directory -Force -Path (Join-Path $steam 'steamapps') | Out-Null
# format reel de Steam : antislashs doubles
$vdf = "`"libraryfolders`"`n{`n`t`"0`"`n`t{`n`t`t`"path`"`t`t`"" + $steam.Replace('\', '\\') + "`"`n`t}`n`t`"1`"`n`t{`n`t`t`"path`"`t`t`"" + $bibli.Replace('\', '\\') + "`"`n`t}`n}"
Set-Content -LiteralPath (Join-Path $steam 'steamapps\libraryfolders.vdf') -Value $vdf
Set-Content -LiteralPath (Join-Path $bibli 'steamapps\appmanifest_3240220.acf') `
    -Value "`"AppState`"`n{`n`t`"appid`"`t`t`"3240220`"`n`t`"installdir`"`t`t`"Grand Theft Auto V Enhanced`"`n}"
$r = @(Chercher @{ Steam = @($steam) })
if ($r.Count -eq 1 -and $r[0].Dossier -ieq $jeu3 -and $r[0].Source -eq 'Steam') {
    Ok 'Steam : bibliotheque sur un autre disque, lue dans libraryfolders.vdf'
} else { Echec ("Steam : " + (($r | ForEach-Object { $_.Dossier }) -join ' | ')) }

# --- 4. aucun launcher : la recherche sur disque ------------------------------
$disque = Join-Path $T 'G'
$jeu4 = Jeu (Join-Path $disque 'MesJeux\Rockstar\GTA5E')
$r = @(Chercher @{ SansScan = $false; Disques = @($disque) })
if ($r.Count -eq 1 -and $r[0].Dossier -ieq $jeu4 -and $r[0].Source -eq 'recherche') {
    Ok 'sans launcher : jeu trouve par la recherche, trois niveaux sous la racine'
} else { Echec 'la recherche sur disque ne trouve pas le jeu' }

# --- 5. reste de desinstallation : ecarte ------------------------------------
$pd5 = Join-Path $T 'pd5'
$vide = Join-Path $T 'H\GTAV'
New-Item -ItemType Directory -Force -Path $vide | Out-Null
Json @{ DisplayName = 'Grand Theft Auto V Enhanced'; InstallLocation = $vide } `
     (Join-Path $pd5 'Epic\EpicGamesLauncher\Data\Manifests\OLD.item')
$r = @(Chercher @{ ProgramData = $pd5 })
if ($r.Count -eq 0) { Ok 'un dossier sans GTA5_Enhanced.exe est ecarte (reste de desinstallation)' }
else { Echec 'un dossier vide est pris pour le jeu' }

# --- 6. Legacy : ecarte -------------------------------------------------------
$pd6 = Join-Path $T 'pd6'
$legacy = Jeu (Join-Path $T 'I\GTAV') 'GTA5.exe'
Json @{ DisplayName = 'Grand Theft Auto V'; InstallLocation = $legacy } `
     (Join-Path $pd6 'Epic\EpicGamesLauncher\Data\Manifests\LEG.item')
$r = @(Chercher @{ ProgramData = $pd6 })
if ($r.Count -eq 0) { Ok 'la version Legacy (GTA5.exe) n est pas prise pour Enhanced' }
else { Echec 'une installation Legacy est prise pour Enhanced' }

# --- 7. le meme jeu vu deux fois ne compte qu une fois -----------------------
$r = @(Chercher @{ ProgramData = $pd1; SansScan = $false; Disques = @((Join-Path $T 'D')) })
if ($r.Count -eq 1) { Ok 'le meme jeu, vu par Epic et par la recherche, ne compte qu une fois' }
else { Echec ("doublon : {0} candidats pour un seul jeu" -f $r.Count) }

# --- 8. la recherche ne se lance pas si un launcher a deja repondu -----------
$sw = [Diagnostics.Stopwatch]::StartNew()
$r = @(Chercher @{ ProgramData = $pd1; SansScan = $false; Disques = @('C:\') })
$sw.Stop()
if ($r.Count -eq 1 -and $sw.ElapsedMilliseconds -lt 3000) {
    Ok ("launcher trouve : pas de recherche sur disque inutile ({0} ms)" -f $sw.ElapsedMilliseconds)
} else { Echec ("recherche lancee alors qu Epic avait repondu ({0} ms)" -f $sw.ElapsedMilliseconds) }

# --- 9. plusieurs jeux sur la meme machine -----------------------------------
$r = @(Chercher @{ ProgramData = $pd1; Steam = @($steam) })
if ($r.Count -eq 2) { Ok 'deux installations (Epic et Steam) sont toutes deux proposees' }
else { Echec ("deux installations attendues, {0} trouvees" -f $r.Count) }

Get-ChildItem -LiteralPath $T -Recurse -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $T -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ''
if ($echecs -eq 0) { Write-Host '=== INSTALLATION : TOUS LES TESTS PASSENT ===' -ForegroundColor Green }
else { Write-Host ("=== INSTALLATION : {0} ECHEC(S) ===" -f $echecs) -ForegroundColor Red; exit 1 }
