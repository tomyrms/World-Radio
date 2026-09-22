$ErrorActionPreference = 'Stop'
$W    = Split-Path -Parent $PSScriptRoot
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'build\trouve-jeu.ps1')
$G    = Trouve-Jeu
$sc   = Join-Path $G 'scripts'
$src  = Join-Path $W 'config'

Write-Host ''
Write-Host '=== World Radio : installation ===' -ForegroundColor Cyan

# --- attendre la fermeture du jeu : les DLL chargees sont verrouillees ---
$attente = 0
while (Get-Process -Name GTA5, GTA5_Enhanced -ErrorAction SilentlyContinue) {
    if ($attente -eq 0) {
        Write-Host ''
        Write-Host 'GTA V tourne encore. Ferme le jeu, l installation part toute seule.' -ForegroundColor Yellow
    }
    Start-Sleep -Seconds 2
    $attente += 2
    if ($attente % 30 -eq 0) { Write-Host ("  ... en attente depuis {0} s" -f $attente) -ForegroundColor DarkGray }
    if ($attente -ge 600) { Write-Host 'Abandon apres 10 minutes.' -ForegroundColor Red; exit 1 }
}

# ============================================================================
#  1. Mise de cote des anciens mods de radio
#     On DEPLACE, on ne supprime pas : le .lfmpack pese 286 Mo et serait
#     long a retelecharger si tu voulais revenir en arriere.
# ============================================================================
$sauve = Join-Path $G ('_anciens_mods_radio_' + (Get-Date -Format 'yyyy-MM-dd'))
$aDeplacer = @(
    'LeonidaFM.dll', 'LeonidaFM.ini',
    'LeonidaFM.dll.desactive_par_StreamFM', 'LeonidaFM.ini.desactive_par_StreamFM',
    'LeonidaFM',
    'VIRadioOnly.dll', 'VIRadioOnly.ini', 'VIRadioOnly',
    'StreamFM.ini', 'StreamFM.log', 'StreamFM.cs.remplace_par_dll', 'StreamFM',
    'StreamFM.cs'
)

$deplaces = 0
foreach ($nom in $aDeplacer) {
    $chemin = Join-Path $sc $nom
    if (-not (Test-Path -LiteralPath $chemin)) { continue }
    if (-not (Test-Path -LiteralPath $sauve)) { New-Item -ItemType Directory -Path $sauve -Force | Out-Null }
    try {
        Move-Item -LiteralPath $chemin -Destination (Join-Path $sauve $nom) -Force
        Write-Host ('  mis de cote : ' + $nom) -ForegroundColor DarkGray
        $deplaces++
    } catch {
        Write-Host ('  IMPOSSIBLE de deplacer ' + $nom + ' : ' + $_.Exception.Message) -ForegroundColor Red
    }
}
if ($deplaces -gt 0) { Write-Host ('  -> ' + $deplaces + ' element(s) dans ' + $sauve) -ForegroundColor Yellow }
else { Write-Host '  rien a mettre de cote' -ForegroundColor DarkGray }

# ============================================================================
#  2. Installation de World Radio
# ============================================================================
Write-Host ''
$dossierIcones = Join-Path $sc 'WorldRadio'
if (-not (Test-Path -LiteralPath $dossierIcones)) {
    New-Item -ItemType Directory -Path $dossierIcones -Force | Out-Null
}

# --- reglages personnels a preserver ---
# Le .ini livre remplace celui du jeu, sinon les nouvelles cles n'arriveraient
# jamais. Mais certaines valeurs appartiennent au joueur : elles sont relues
# avant l'ecrasement, puis reecrites apres.
$aPreserver = @('Volume', 'LastStation', 'MuteKey', 'VolumeUpKey', 'VolumeDownKey',
                'NextStationKey', 'PrevStationKey', 'NowPlaying', 'NowPlayingSeconds',
                'InvertMenuAxis', 'AudioLog', 'NowPlayingRaise', 'WheelSlowMotion', 'DuckDuringDialogue', 'DuckOnAmbientSpeech')
$personnels = @{}
$iniJeu = Join-Path $sc 'WorldRadio.ini'
if (Test-Path -LiteralPath $iniJeu) {
    foreach ($ligne in Get-Content -LiteralPath $iniJeu) {
        $l = $ligne.Trim()
        if ($l.Length -eq 0 -or $l[0] -eq ';' -or $l[0] -eq '#') { continue }
        $eq = $l.IndexOf('=')
        if ($eq -le 0) { continue }
        $cle = $l.Substring(0, $eq).Trim()
        if ($aPreserver -contains $cle) { $personnels[$cle] = $l.Substring($eq + 1).Trim() }
    }
}

$copies = @(
    @{ de = (Join-Path $src 'WorldRadio.dll'); vers = (Join-Path $sc 'WorldRadio.dll') },
    @{ de = (Join-Path $src 'WorldRadio.ini'); vers = (Join-Path $sc 'WorldRadio.ini') },
    @{ de = (Join-Path $W  'naudio\NAudio.dll'); vers = (Join-Path $sc 'NAudio.dll') }
)
foreach ($c in $copies) {
    if (-not (Test-Path -LiteralPath $c.de)) { throw ('source manquante : ' + $c.de) }
    Copy-Item -LiteralPath $c.de -Destination $c.vers -Force
    Write-Host ('  {0,-20} {1,10:N0} o' -f (Split-Path $c.vers -Leaf), (Get-Item -LiteralPath $c.vers).Length) -ForegroundColor Green
}

# --- on rend au joueur ses reglages ---
if ($personnels.Count -gt 0) {
    $lignes = Get-Content -LiteralPath $iniJeu
    $rendus = 0
    for ($i = 0; $i -lt $lignes.Count; $i++) {
        $l = $lignes[$i].Trim()
        if ($l.Length -eq 0 -or $l[0] -eq ';' -or $l[0] -eq '#') { continue }
        $eq = $l.IndexOf('=')
        if ($eq -le 0) { continue }
        $cle = $l.Substring(0, $eq).Trim()
        if (-not $personnels.ContainsKey($cle)) { continue }
        if ($lignes[$i] -eq ($cle + '=' + $personnels[$cle])) { continue }
        $lignes[$i] = $cle + '=' + $personnels[$cle]
        $rendus++
    }
    # LastStation n'existe pas dans le .ini livre : on l'ajoute
    if ($personnels.ContainsKey('LastStation') -and
        -not ($lignes | Where-Object { $_.TrimStart().StartsWith('LastStation=') })) {
        $lignes = @($lignes[0..1]) + @('LastStation=' + $personnels['LastStation']) + @($lignes[2..($lignes.Count-1)])
        $rendus++
    }
    if ($rendus -gt 0) {
        Set-Content -LiteralPath $iniJeu -Value $lignes -Encoding UTF8
        Write-Host ('  {0} reglage(s) personnel(s) preserve(s)' -f $rendus) -ForegroundColor Yellow
    }
}

Get-ChildItem -LiteralPath (Join-Path $src 'icons') -Filter '*.png' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $dossierIcones $_.Name) -Force
    Write-Host ('  {0,-20} {1,10:N0} o' -f $_.Name, $_.Length) -ForegroundColor Green
}

# ============================================================================
#  3. Controle final
# ============================================================================
Write-Host ''
Write-Host '--- controle ---' -ForegroundColor Cyan
$manque = $false
foreach ($f in 'WorldRadio.dll', 'WorldRadio.ini', 'NAudio.dll') {
    $p = Join-Path $sc $f
    if (Test-Path -LiteralPath $p) { Write-Host ('  ok      ' + $f) -ForegroundColor Green }
    else { Write-Host ('  MANQUE  ' + $f) -ForegroundColor Red; $manque = $true }
}
$n = (Get-ChildItem -LiteralPath $dossierIcones -Filter '*.png' -ErrorAction SilentlyContinue).Count
if ($n -ge 19) { Write-Host ('  ok      ' + $n + ' images (drapeaux, stations, degrade, vignette)') -ForegroundColor Green }
else { Write-Host ('  MANQUE  des images : ' + $n + ' sur 19') -ForegroundColor Red; $manque = $true }

# aucun ancien mod de radio ne doit subsister : deux mods se disputeraient la radio
foreach ($f in 'LeonidaFM.dll', 'VIRadioOnly.dll') {
    if (Test-Path -LiteralPath (Join-Path $sc $f)) {
        Write-Host ('  RESTE   ' + $f + ' : il entrera en conflit') -ForegroundColor Red
        $manque = $true
    }
}

# journal remis a zero pour un diagnostic propre
$log = Join-Path $sc 'WorldRadio.log'
if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log -Force }

Write-Host ''
if ($manque) { Write-Host 'Installation INCOMPLETE, voir ci-dessus.' -ForegroundColor Red; exit 1 }

Write-Host 'Installation terminee.' -ForegroundColor Cyan
Write-Host ''
Write-Host 'En jeu, au volant :' -ForegroundColor White
Write-Host '  TOUCHE RADIO brievement  eteint, ou rallume la derniere station' -ForegroundColor Green
Write-Host '  TOUCHE RADIO maintenue   ouvre la roue' -ForegroundColor Green
Write-Host '                           (Q au clavier, croix gauche a la manette)'
Write-Host '    stick droit            VISE une station  -  on pointe, on ne defile pas'
Write-Host '    tout en haut           RADIO OFF, a la meme place dans chaque pays'
Write-Host '    R1                     pays suivant, en boucle'
Write-Host '    relacher               fermer'
Write-Host '  La station visee se lance aussitot : on entend ce qu on pointe.'
Write-Host '  Le jeu passe au ralenti pendant le choix, comme la roue d origine'
Write-Host '  (WheelSlowMotion=1.00 dans le .ini pour desactiver).'
Write-Host ''
Write-Host '  F10 / F9                 station suivante / precedente'
Write-Host '  NumPad9 / NumPad3        volume par 10 % (memorise dans le .ini)'
Write-Host '  NumPad0                  eteint, ou rallume la derniere station'
Write-Host ''
Write-Host '  L encart en bas a droite donne le logo, le pays, la station,'
Write-Host '  puis l artiste et le titre lus dans le flux lui-meme.'
Write-Host '  S il chevauche le nom de quartier de GTA, augmente NowPlayingRaise'
Write-Host '  dans WorldRadio.ini.'
Write-Host ''
Write-Host '  La radio demarre eteinte. Elle se tait en pause, en arriere-plan'
Write-Host '  et a pied, et eteint la radio du vehicule pendant l ecoute.'
Write-Host '  Hors vehicule elle n est ni audible ni pilotable ; seul le volume'
Write-Host '  reste reglable, avec une jauge pour tout retour.'
Write-Host ''
Write-Host ('  Journal : ' + $log) -ForegroundColor DarkGray
Write-Host ''
