# ============================================================================
#  La roue : le stick vise-t-il vraiment ce qu'on croit ?
# ----------------------------------------------------------------------------
#  Une erreur de signe ou de sens enverrait le pouce sur la station d'a cote
#  sans que rien ne le signale : ca se sentirait en jouant, jamais en lisant
#  le code. On verifie donc la correspondance angle -> secteur telle quelle.
# ============================================================================

$ErrorActionPreference = 'Stop'
$W   = Split-Path -Parent $PSScriptRoot
$dll = Join-Path $W 'WorldRadio.dll'

$echecs = 0
function Ok($m)    { Write-Host ('  ok    : ' + $m) -ForegroundColor Green }
function Echec($m) { Write-Host ('  ECHEC : ' + $m) -ForegroundColor Red; $script:echecs++ }

[void][Reflection.Assembly]::LoadFrom((Join-Path $W 'lib\NAudio.dll'))
$a  = [Reflection.Assembly]::LoadFrom($dll)
$NP = [Reflection.BindingFlags]'NonPublic,Public,Instance,Static'

$tMenu = $a.GetType('WorldRadio.Menu')
$mSect = $tMenu.GetMethod('SecteurPour', $NP)
$mAngle = $tMenu.GetMethod('AngleSecteur', $NP)

function Secteur([double]$angle, [int]$total) { [int]$mSect.Invoke($null, @([float]$angle, [int]$total)) }
function Angle([int]$s, [int]$total)          { [double]$mAngle.Invoke($null, @([int]$s, [int]$total)) }

Write-Host ''
Write-Host '=== roue : visee angulaire ===' -ForegroundColor Cyan

$PI = [Math]::PI

# --- RADIO OFF doit etre a midi, quel que soit le nombre de stations -------
$bon = $true
foreach ($n in 2, 3, 4, 5, 6) {
    if ((Secteur 0.0 $n) -ne 0) { $bon = $false }
    # tout juste a gauche et a droite de midi : encore RADIO OFF
    if ((Secteur 0.05 $n) -ne 0) { $bon = $false }
    if ((Secteur (2 * $PI - 0.05) $n) -ne 0) { $bon = $false }
}
if ($bon) { Ok 'RADIO OFF est a midi pour 2 a 6 secteurs, et le reste autour de midi' }
else      { Echec 'RADIO OFF n est pas a midi selon le nombre de secteurs' }

# --- chaque secteur se retrouve a partir de son propre angle ---------------
$bon = $true
$detail = @()
foreach ($n in 2, 3, 4, 5, 6) {
    for ($s = 0; $s -lt $n; $s++) {
        $r = Secteur (Angle $s $n) $n
        if ($r -ne $s) { $bon = $false; $detail += "n=$n s=$s -> $r" }
    }
}
if ($bon) { Ok 'viser le centre d un secteur y renvoie bien, de 2 a 6 secteurs' }
else      { Echec ('centre mal retrouve : ' + ($detail -join ', ')) }

# --- sens horaire : le secteur 1 est a DROITE de midi ----------------------
# 5 secteurs, un pas de 72 degres. A 72 deg (0,4 pi) on doit etre sur le 1.
$s1 = Secteur (0.4 * $PI) 5
if ($s1 -eq 1) { Ok 'le sens est horaire : a 72 deg vers la droite on est sur le secteur 1' }
else           { Echec ("sens inverse : 72 deg donne le secteur $s1 au lieu de 1") }

# et a 288 deg (vers la gauche) on doit etre sur le dernier
$s4 = Secteur (1.6 * $PI) 5
if ($s4 -eq 4) { Ok 'a 288 deg vers la gauche on est sur le dernier secteur' }
else           { Echec ("288 deg donne le secteur $s4 au lieu de 4") }

# --- les frontieres tombent a mi-chemin ------------------------------------
# 4 secteurs, pas de 90 deg : la frontiere 0|1 est a 45 deg.
$avant = Secteur (($PI / 4) - 0.02) 4
$apres = Secteur (($PI / 4) + 0.02) 4
if ($avant -eq 0 -and $apres -eq 1) { Ok 'la frontiere entre deux secteurs tombe bien a mi-chemin' }
else { Echec ("frontiere mal placee : $avant puis $apres autour de 45 deg") }

# --- aucun angle ne sort de la plage ---------------------------------------
$bon = $true
foreach ($n in 2, 3, 4, 5, 6, 13) {
    for ($d = -720; $d -le 720; $d += 7) {
        $r = Secteur ($d * $PI / 180.0) $n
        if ($r -lt 0 -or $r -ge $n) { $bon = $false; break }
    }
}
if ($bon) { Ok 'aucun angle, meme negatif ou au-dela d un tour, ne sort de la plage' }
else      { Echec 'un angle produit un secteur hors plage' }

# --- degenerescence --------------------------------------------------------
if ((Secteur 1.0 0) -eq 0 -and (Secteur 1.0 -3) -eq 0) { Ok 'un nombre de secteurs nul ou negatif ne fait pas tomber le calcul' }
else { Echec 'cas degenere mal traite' }

# --- le ralenti est-il toujours rendu ? ------------------------------------
$src = Get-Content (Join-Path $W 'src\WorldRadio.cs') -Raw

# a la fermeture, la rampe ramene a 1 ; a l arret, on rend le temps d un coup
if ($src -match 'ReglerLeTemps\(ouvert\)') { Ok 'le ralenti est pilote a chaque image, dans les deux sens' }
else { Echec 'rien ne ramene le temps a la normale a la fermeture de la roue' }

if ($src -match 'RendreLeTemps\(\)\s*;' ) { Ok 'la vitesse normale est retablie d un coup a l arret du script' }
else { Echec 'rien ne retablit la vitesse a l arret du script' }

# la rampe doit utiliser le temps REEL : l horloge du jeu etant elle-meme
# ralentie, la sortie du ralenti n en finirait pas
if ($src -match 'Environment\.TickCount' -and $src -match 'Math\.Exp') { Ok 'la rampe est amortie sur le temps reel, pas par image' }
else { Echec 'la rampe du ralenti n est pas amortie sur le temps reel' }

# --- les logos gardent-ils leurs proportions ? -----------------------------
Add-Type -AssemblyName System.Drawing
$icons = Join-Path $W 'assets\icons'
$tProp = $a.GetType('WorldRadio.Proportions')
$tProp.GetMethod('Init', $NP).Invoke($null, @([string]$icons)) | Out-Null
$mContenir = $tProp.GetMethod('Contenir', $NP)

$ecarts = @()
foreach ($f in Get-ChildItem $icons -Filter '*.png' | Where-Object { $_.Name -notmatch 'veil_fade|vignette' }) {
    $img = [Drawing.Image]::FromFile($f.FullName)
    $reel = $img.Width / $img.Height
    $img.Dispose()

    $args2 = [object[]]@($f.Name, [float]64, [float]0, [float]0)
    $mContenir.Invoke($null, $args2) | Out-Null
    $rendu = $args2[2] / $args2[3]

    if ([Math]::Abs($rendu - $reel) -gt 0.02) { $ecarts += ('{0} : reel {1:N2} rendu {2:N2}' -f $f.Name, $reel, $rendu) }
}
if ($ecarts.Count -eq 0) { Ok ('les {0} images gardent leurs proportions au dessin' -f (Get-ChildItem $icons -Filter '*.png').Count) }
else { Echec ('images deformees : ' + ($ecarts -join ' | ')) }

# une image large ne doit surtout pas ressortir carree
$args3 = [object[]]@('station_COMERCIAL.png', [float]64, [float]0, [float]0)
$mContenir.Invoke($null, $args3) | Out-Null
if ($args3[2] -gt $args3[3] * 2) { Ok ('RADIO COMERCIAL reste large : {0:N0}x{1:N0} dans une boite de 64' -f $args3[2], $args3[3]) }
else { Echec 'RADIO COMERCIAL ressort presque carre : le logo est ecrase' }

# --- un seul bouton pour le pays -------------------------------------------
$menuSrc = Get-Content (Join-Path $W 'src\Menu.cs') -Raw
if ($menuSrc -notmatch 'VehicleHorn') { Ok 'plus aucune reference a VehicleHorn : seul R1 change de pays' }
else { Echec 'VehicleHorn est encore reference' }

# --- la sequence station -> off -> meme station ----------------------------
#  Le defaut : radio eteinte, le curseur se posait sur la derniere station et
#  le menu l'enregistrait comme cible courante. La reviser ne faisait donc
#  rien, il fallait passer par une autre station pour casser l'egalite.
$tCfg = $a.GetType('WorldRadio.Config')
$dossierCfg = [string](Join-Path $W 'config')
$cfg = $tCfg.GetMethod('Charger', $NP).Invoke($null, @($dossierCfg))
$stations = $tCfg.GetField('Stations', $NP).GetValue($cfg)

$tMenu2 = $a.GetType('WorldRadio.Menu')
$menu = [Activator]::CreateInstance($tMenu2, $true)
$mPos = $tMenu2.GetMethod('PositionnerSur', $NP)
$mCible = $tMenu2.GetMethod('Cible', $NP)
$fCible = $tMenu2.GetField('_derniereCible', $NP)
$fSecteur = $tMenu2.GetField('_secteur', $NP)

# cas 1 : radio ETEINTE, curseur sur la station 0
$mPos.Invoke($menu, @($cfg, [int]0, [int](-1))) | Out-Null
$dep = [int]$fCible.GetValue($menu)
$vise = [int]$mCible.Invoke($menu, @($cfg))
if ($dep -eq -1) { Ok 'radio eteinte : la cible de depart est bien « rien »' }
else { Echec "radio eteinte : cible de depart $dep au lieu de -1" }
if ($vise -ne $dep) { Ok ("viser la derniere station declenche bien (cible {0} != depart {1})" -f $vise, $dep) }
else { Echec 'viser la derniere station ne declencherait rien : le bug est toujours la' }

# cas 2 : la station 0 JOUE deja, la reviser ne doit rien relancer
$mPos.Invoke($menu, @($cfg, [int]0, [int]0)) | Out-Null
$dep2 = [int]$fCible.GetValue($menu)
$vise2 = [int]$mCible.Invoke($menu, @($cfg))
if ($dep2 -eq $vise2) { Ok 'station deja en cours : la reviser ne la relance pas' }
else { Echec "station en cours : depart $dep2, visee $vise2, elle serait relancee pour rien" }

# --- la roue ne decide rien tant qu on n a rien fait ------------------------
$menuSrc2 = Get-Content (Join-Path $W 'src\Menu.cs') -Raw
if ($menuSrc2 -match 'if \(!_aInteragi\) return ActionMenu\.Aucune') {
    Ok 'ouvrir la roue ne relance rien tant que le stick n a pas bouge'
} else {
    Echec 'la roue peut agir sans que le joueur ait rien fait'
}

# --- un seul mecanisme d extinction ----------------------------------------
$audioSrc = Get-Content (Join-Path $W 'src\Audio.cs') -Raw
$jeuSrc = Get-Content (Join-Path $W 'src\WorldRadio.cs') -Raw
if ($audioSrc -notmatch '_coupeParLeJoueur' -and $jeuSrc -notmatch 'BasculerCoupure') {
    Ok 'la coupure volontaire a disparu : RADIO OFF est le seul mecanisme'
} else {
    Echec 'il reste deux facons d eteindre la radio'
}
if ($jeuSrc -match '_avantExtinction') {
    Ok 'la station d avant l extinction est memorisee pour le retour'
} else {
    Echec 'rien ne memorise la station a rallumer'
}

Write-Host ''
if ($echecs -eq 0) { Write-Host '=== ROUE : TOUS LES TESTS PASSENT ===' -ForegroundColor Green }
else { Write-Host ("=== ROUE : {0} ECHEC(S) ===" -f $echecs) -ForegroundColor Red; exit 1 }
