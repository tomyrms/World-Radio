# ============================================================================
#  Preuve que les barres lisent du vrai son
# ----------------------------------------------------------------------------
#  Une barre qui bouge ne prouve rien : elle pourrait etre animee toute seule.
#  Ce test branche le vrai lecteur sur une vraie station et verifie que :
#
#     1. la sonde recoit des echantillons non nuls
#     2. les cinq bandes repondent, et pas toutes pareil
#     3. les niveaux VARIENT dans le temps (un son fige serait suspect)
#     4. a l'arret, les barres retombent au lieu de rester figees
# ============================================================================

$ErrorActionPreference = 'Stop'
$W    = Split-Path -Parent $PSScriptRoot
$dll  = Join-Path $W 'WorldRadio.dll'
$naud = Join-Path $W 'lib\NAudio.dll'

$echecs = 0
function Ok($m)     { Write-Host ('  ok    : ' + $m) -ForegroundColor Green }
function Echec($m)  { Write-Host ('  ECHEC : ' + $m) -ForegroundColor Red; $script:echecs++ }

[void][Reflection.Assembly]::LoadFrom($naud)
$a = [Reflection.Assembly]::LoadFrom($dll)

$NP = [Reflection.BindingFlags]'NonPublic,Public,Instance,Static'

$tLecteur = $a.GetType('WorldRadio.Lecteur')
$tSpectre = $a.GetType('WorldRadio.Spectre')
$tSonde   = $a.GetType('WorldRadio.SondeSpectre')

Write-Host ''
Write-Host '=== spectre : lecture d un flux reel ===' -ForegroundColor Cyan

foreach ($t in @($tLecteur, $tSpectre, $tSonde)) {
    if ($null -eq $t) { Echec 'type manquant dans la DLL'; exit 1 }
}
Ok 'Lecteur, Spectre et SondeSpectre presents'

# --- la sonde est-elle bien AVANT le gain ? --------------------------------
$src = Get-Content (Join-Path $W 'src\Lecteur.cs') -Raw
if ($src -match 'new\s+SondeSpectre\(\s*echantillons\s*\)' -and
    $src -match 'new\s+VolumeSampleProvider\(\s*sonde\s*\)') {
    Ok 'chaine : source -> sonde -> gain -> sortie (la sonde precede le gain)'
} else {
    Echec 'la sonde n est pas placee avant l etage de gain'
}

# --- lecture reelle ---------------------------------------------------------
$lecteur = [Activator]::CreateInstance($tLecteur, $true)
$mJouer  = $tLecteur.GetMethod('Jouer', $NP)
$mArret  = $tLecteur.GetMethod('Eteindre', $NP)
$mGain   = $tLecteur.GetMethod('DefinirGain', $NP)
$pSonde  = $tLecteur.GetProperty('Sonde', $NP)
$pFreq   = $tLecteur.GetProperty('Frequence', $NP)

# gain nul : le test ne doit rien faire entendre, la sonde est en amont
$mGain.Invoke($lecteur, @([float]0.0)) | Out-Null
$mJouer.Invoke($lecteur, @('https://icecast.skyrock.net/s/natio_mp3_128k')) | Out-Null

$sonde = $null
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 250
    $sonde = $pSonde.GetValue($lecteur)
    if ($null -ne $sonde) { break }
}
if ($null -eq $sonde) { Echec 'aucune sonde apres 15 s : le flux ne s est pas ouvert'; exit 1 }

$freq = $pFreq.GetValue($lecteur)
Ok ("flux ouvert, sonde en place  -  {0} Hz" -f $freq)

Start-Sleep -Milliseconds 1200        # laisser le tampon se remplir

# --- des echantillons arrivent-ils vraiment ? ------------------------------
$mCopier = $tSonde.GetMethod('Copier', $NP)
$taille  = [int]$tSonde.GetField('Taille', $NP).GetValue($null)
$buf     = [float[]]::new($taille)
$mCopier.Invoke($sonde, @(,$buf)) | Out-Null

$nonNuls = 0; $crete = 0.0
foreach ($v in $buf) { if ([Math]::Abs($v) -gt 1e-6) { $nonNuls++ }
                       if ([Math]::Abs($v) -gt $crete) { $crete = [Math]::Abs($v) } }
if ($nonNuls -gt ($taille / 2)) {
    Ok ("echantillons recus : {0}/{1} non nuls, crete {2:N3}" -f $nonNuls, $taille, $crete)
} else {
    Echec ("tampon quasi vide : {0}/{1} non nuls" -f $nonNuls, $taille)
}

# --- les bandes repondent-elles ? -------------------------------------------
$spectre    = [Activator]::CreateInstance($tSpectre, $true)
$mRafraichir = $tSpectre.GetMethod('Rafraichir', $NP)
$mNiveau     = $tSpectre.GetMethod('Niveau', $NP)
$bandes      = [int]$tSpectre.GetField('Bandes', $NP).GetValue($null)

$releves = @()
for ($i = 0; $i -lt 25; $i++) {
    $mRafraichir.Invoke($spectre, @($sonde, [int]$freq, $true)) | Out-Null
    $ligne = @()
    for ($b = 0; $b -lt $bandes; $b++) { $ligne += [double]$mNiveau.Invoke($spectre, @([int]$b)) }
    $releves += ,$ligne
    Start-Sleep -Milliseconds 120
}

$dernier = $releves[-1]
Write-Host ('        bandes  : ' + (($dernier | ForEach-Object { '{0,5:N2}' -f $_ }) -join ' ')) -ForegroundColor DarkGray

$actives = ($dernier | Where-Object { $_ -gt 0.02 }).Count
if ($actives -ge 3) { Ok ("{0} bandes sur {1} repondent" -f $actives, $bandes) }
else { Echec ("seules {0} bandes sur {1} repondent" -f $actives, $bandes) }

# toutes identiques = un signal plat, donc pas de vraie analyse frequentielle
$ecart = ($dernier | Measure-Object -Maximum -Minimum)
$delta = $ecart.Maximum - $ecart.Minimum
if ($delta -gt 0.05) { Ok ("les bandes different entre elles (ecart {0:N2}) : l analyse est bien frequentielle" -f $delta) }
else { Echec ("toutes les bandes sont au meme niveau (ecart {0:N2})" -f $delta) }

# --- les niveaux varient-ils dans le temps ? --------------------------------
$variation = 0.0
for ($b = 0; $b -lt $bandes; $b++) {
    $col = $releves | ForEach-Object { $_[$b] }
    $m = ($col | Measure-Object -Maximum -Minimum)
    $variation += ($m.Maximum - $m.Minimum)
}
if ($variation -gt 0.10) { Ok ("les niveaux bougent au fil du temps (somme des amplitudes {0:N2})" -f $variation) }
else { Echec ("niveaux figes sur 3 s (somme des amplitudes {0:N2})" -f $variation) }

# --- coupure : les barres doivent retomber ----------------------------------
for ($i = 0; $i -lt 40; $i++) {
    $mRafraichir.Invoke($spectre, @($sonde, [int]$freq, $false)) | Out-Null
}
$apres = 0.0
for ($b = 0; $b -lt $bandes; $b++) { $apres += [double]$mNiveau.Invoke($spectre, @([int]$b)) }
if ($apres -lt 0.05) { Ok ("a la coupure les barres retombent (total {0:N3})" -f $apres) }
else { Echec ("les barres restent hautes une fois coupe (total {0:N3})" -f $apres) }

$mArret.Invoke($lecteur, @()) | Out-Null

Write-Host ''
if ($echecs -eq 0) { Write-Host '=== SPECTRE : TOUS LES TESTS PASSENT ===' -ForegroundColor Green }
else { Write-Host ("=== SPECTRE : {0} ECHEC(S) ===" -f $echecs) -ForegroundColor Red; exit 1 }
