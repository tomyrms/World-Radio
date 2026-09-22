# ============================================================================
#  Les fondus, et le verrou qui collait
# ----------------------------------------------------------------------------
#  Le defaut corrige ici : une cinematique armait le verrou du menu pause, qui
#  ne se levait qu'a la premiere commande pressee. Approcher d'une mission
#  suffisait donc a eteindre la radio pour de bon. Un test structurel garantit
#  que les deux signaux ne sont plus melanges.
#
#  La rampe, elle, se mesure : on la fait tourner en temps reel et on regarde
#  ou elle arrive.
# ============================================================================

$ErrorActionPreference = 'Stop'
$W = Split-Path -Parent $PSScriptRoot

$echecs = 0
function Ok($m)    { Write-Host ('  ok    : ' + $m) -ForegroundColor Green }
function Echec($m) { Write-Host ('  ECHEC : ' + $m) -ForegroundColor Red; $script:echecs++ }

[void][Reflection.Assembly]::LoadFrom("$W\lib\NAudio.dll")
$a  = [Reflection.Assembly]::LoadFrom("$W\WorldRadio.dll")
$NP = [Reflection.BindingFlags]'NonPublic,Public,Instance,Static'

Write-Host ''
Write-Host '=== verrou de pause et fondus ===' -ForegroundColor Cyan

# --- 1. le verrou ne doit plus connaitre les cinematiques -------------------
$src = Get-Content "$W\src\Audio.cs" -Raw
$d = $src.IndexOf('private void LireMenuPause()')
$f = $src.IndexOf('private void Sonder()')
$bloc = $src.Substring($d, $f - $d)

if ($bloc -notmatch 'SceneCinematique') {
    Ok 'le verrou collant ne regarde plus les cinematiques'
} else {
    Echec 'LireMenuPause regarde encore SceneCinematique : le verrou collera'
}
if ($bloc -match 'MenuPauseOuvert') { Ok 'il regarde bien le menu pause' }
else { Echec 'le verrou ne regarde plus le menu pause du tout' }

# --- 2. les cinematiques sont lues en continu, ailleurs ---------------------
$d2 = $src.IndexOf('internal void Rafraichir(')
$f2 = $src.IndexOf('private const int RaisonAucune')
$bloc2 = $src.Substring($d2, $f2 - $d2)
if ($bloc2 -match 'EnMission' -and $bloc2 -match 'DialogueEnCours') {
    Ok 'mission et dialogue sont evalues a chaque image'
} else {
    Echec 'mission et dialogue ne sont pas evalues en continu'
}
# la proximite d un declencheur ne doit PLUS rien couper
if ($src -notmatch 'JoueurAuxCommandes') {
    Ok 'IS_PLAYER_CONTROL_ON n est plus consulte : plus de coupure pres d un point'
} else {
    Echec 'JoueurAuxCommandes est encore utilise : la radio coupera pres des missions'
}
# et mission/dialogue doivent ATTENUER, pas couper
if ($bloc2 -match 'baisser \? _attenuation : 1f') {
    Ok 'mission et dialogue attenuent le volume au lieu de le couper'
} else {
    Echec 'mission et dialogue coupent encore au lieu d attenuer'
}
# une cinematique, elle, doit couper completement
if ($bloc2 -match 'cinematique\s*\)\s*\{\s*raison = RaisonCinematique') {
    Ok 'une cinematique coupe completement, elle n attenue pas'
} else {
    Echec 'la cinematique ne coupe pas : elle devrait mettre le son a zero'
}
if ($bloc2 -notmatch 'baisser = .*SceneCinematique') {
    Ok 'la cinematique ne figure plus parmi les causes d attenuation'
} else {
    Echec 'la cinematique attenue encore au lieu de couper'
}
if ($bloc2 -match 'AuPremierPlan') { Ok 'le focus de la fenetre est enfin consulte' }
else { Echec 'le focus fenetre est toujours ignore' }

# --- 3. la rampe est-elle fondee sur le temps ? -----------------------------
$d3 = $src.IndexOf('private void Avancer()')
$f3 = $src.IndexOf('private int _instantRampe;')
$bloc3 = $src.Substring($d3, $f3 - $d3)
if ($bloc3 -match 'TickCount' -and $bloc3 -match '_dureeFondu') {
    Ok 'la rampe est calculee sur le temps ecoule, pas par image'
} else {
    Echec 'la rampe depend encore du nombre d images par seconde'
}

# --- 4. mesure reelle d un fondu --------------------------------------------
$tL = $a.GetType('WorldRadio.Lecteur')
$tC = $a.GetType('WorldRadio.ControleurAudio')
$lecteur = [Activator]::CreateInstance($tL, $true)
$ctrl = $tC.GetConstructors($NP)[0].Invoke(@($lecteur))

$fGainActuel = $tC.GetField('_gainActuel', $NP)
$fGainCible  = $tC.GetField('_gainCible', $NP)
$fDuree      = $tC.GetField('_dureeFondu', $NP)
$fInstant    = $tC.GetField('_instantRampe', $NP)
$mAvancer    = $tC.GetMethod('Avancer', $NP)

function Mesurer([int]$duree) {
    $fGainActuel.SetValue($ctrl, [float]1.0)
    $fGainCible.SetValue($ctrl, [float]0.0)
    $fDuree.SetValue($ctrl, [int]$duree)
    $fInstant.SetValue($ctrl, [int][Environment]::TickCount)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ([float]$fGainActuel.GetValue($ctrl) -gt 0.0 -and $sw.ElapsedMilliseconds -lt 3000) {
        Start-Sleep -Milliseconds 8
        $mAvancer.Invoke($ctrl, @()) | Out-Null
    }
    $sw.Stop()
    return $sw.ElapsedMilliseconds
}

foreach ($cas in @(@{n='attenuation';d=420}, @{n='focus';d=130})) {
    $mesure = Mesurer $cas.d
    $ecart = [Math]::Abs($mesure - $cas.d)
    if ($ecart -le [Math]::Max(60, $cas.d * 0.35)) {
        Ok ("fondu {0} : demande {1} ms, mesure {2} ms" -f $cas.n, $cas.d, $mesure)
    } else {
        Echec ("fondu {0} : demande {1} ms, mesure {2} ms" -f $cas.n, $cas.d, $mesure)
    }
}

# duree nulle : le menu pause doit couper en une seule passe
$fGainActuel.SetValue($ctrl, [float]1.0)
$fGainCible.SetValue($ctrl, [float]0.0)
$fDuree.SetValue($ctrl, [int]0)
$fInstant.SetValue($ctrl, [int][Environment]::TickCount)
$mAvancer.Invoke($ctrl, @()) | Out-Null
if ([float]$fGainActuel.GetValue($ctrl) -eq 0.0) { Ok 'le menu pause coupe en une seule image' }
else { Echec 'le menu pause ne coupe pas immediatement' }

$mDuree  = $tC.GetMethod('Duree', $NP)
$mRetour = $tC.GetMethod('DureeRetour', $NP)

# --- 5. la remontee doit etre plus lente que la descente --------------------
$noms = @{ 1='pause'; 2='focus'; 3='cinematique'; 4='hors vehicule'; 5='coupure'; 6='station'; 7='ecran noir'; 8='hors jeu'; 9='bascule'; 10='vehicule' }
$bon = $true
foreach ($r in 2, 3, 4, 5, 6, 7, 8, 9, 10) {
    $bas = [int]$mDuree.Invoke($null, @([int]$r))
    $haut = [int]$mRetour.Invoke($null, @([int]$r))
    if ($haut -lt $bas) { $bon = $false; Echec ("{0} : remontee {1} ms plus rapide que descente {2} ms" -f $noms[$r], $haut, $bas) }
}
if ($bon) { Ok 'toute remontee est plus lente que sa descente' }


# --- 4 bis. la bonne native de cinematique ---------------------------------
#  IS_CUTSCENE_ACTIVE devient vrai des qu'une cinematique est CHARGEE : GTA la
#  precharge en approchant d'un declencheur, donc la radio se coupait en
#  passant simplement a cote. IS_CUTSCENE_PLAYING n'est vrai qu'en lecture.
$natifSrc = Get-Content "$W\src\Natif.cs" -Raw
$dS = $natifSrc.IndexOf('internal static bool SceneCinematique()')
$fS = $natifSrc.IndexOf("`n        }", $dS)
$blocS = $natifSrc.Substring($dS, $fS - $dS)
if ($blocS -match 'IS_CUTSCENE_PLAYING') {
    Ok 'la cinematique est detectee par IS_CUTSCENE_PLAYING'
} else {
    Echec 'mauvaise native : la radio coupera pres des declencheurs de mission'
}
if ($blocS -notmatch 'IsCutsceneActive') {
    Ok 'Game.IsCutsceneActive n est plus utilise'
} else {
    Echec 'Game.IsCutsceneActive est encore la : il repond au prechargement'
}

# --- 4 ter. toutes les situations sont-elles couvertes ? -------------------
$attendus = @{
    'EcranNoir'            = 'ecran noir : chargement, teleportation, fondu'
    'JoueurHorsJeu'        = 'mort ou arrestation'
    'ChangementPersonnage' = 'bascule entre les trois personnages'
    'VehiculeInapte'       = 'moteur coupe, velo, epave, vehicule sous l eau'
}
foreach ($k in $attendus.Keys) {
    if ($bloc2 -match [regex]::Escape($k)) { Ok ("pris en compte : " + $attendus[$k]) }
    else { Echec ("jamais consulte : " + $attendus[$k]) }
}

# les repliques d ambiance existent mais ne doivent PAS etre actives d office
$cfg = Get-Content "$W\config\WorldRadio.ini" -Raw
if ($cfg -match '(?m)^DuckOnAmbientSpeech=false') {
    Ok 'les repliques d ambiance sont disponibles mais desactivees par defaut'
} else {
    Echec 'DuckOnAmbientSpeech devrait etre a false dans le fichier livre'
}

# chaque cause doit avoir une duree qui lui est propre
$dur = @{}
$bon2 = $true
foreach ($r in 1, 2, 3, 4, 5, 6, 7, 8, 9, 10) {
    $d = [int]$mDuree.Invoke($null, @([int]$r))
    $u = [int]$mRetour.Invoke($null, @([int]$r))
    if ($d -lt 0 -or $u -le 0) { $bon2 = $false; Echec ("duree absurde pour la cause $r : $d / $u") }
}
if ($bon2) { Ok 'les dix causes ont toutes des durees de fondu valides' }

# --- 5 bis. l appui bref ne doit pas se bloquer ----------------------------
#  Le defaut : une sortie anticipee laissait un horodatage perime, l appui
#  suivant paraissait long, et la bascule ne partait plus jamais.
$menuSrc = Get-Content "$W\src\Menu.cs" -Raw
# on lit jusqu'a l'accolade fermante, pas une fenetre de taille fixe :
# une ligne ajoutee suffisait a faire sortir l'instruction du champ
$dF = $menuSrc.IndexOf('internal void Fermer()')
$fF = $menuSrc.IndexOf('
        }', $dF)
$blocF = $menuSrc.Substring($dF, $fF - $dF)
if ($blocF -match '_instantAppui = int\.MinValue') {
    Ok 'la fermeture remet l horodatage de l appui a zero'
} else {
    Echec 'Fermer() ne remet pas l horodatage : la bascule se bloquera apres un temps'
}

# --- 6. le chien de garde coupe quand le jeu ne tourne plus ----------------
#  C'est LA correction de fond : pendant le menu pause GTA gele le fil des
#  scripts, donc le mod ne peut pas baisser son propre volume. Ce fil-ci ne
#  gele pas. On simule un jeu qui s'arrete en cessant d'avancer la date.
$fDernierTick = $tC.GetField('_dernierTick', $NP)
$fCoupeGarde  = $tC.GetField('_coupeParLaGarde', $NP)
$fGain        = $tL.GetField('_gain', $NP)
$mDemarrer    = $tC.GetMethod('DemarrerGarde', $NP)

$fGainActuel.SetValue($ctrl, [float]0.8)
$fGain.SetValue($lecteur, [float]0.8)
$fDernierTick.SetValue($ctrl, [int][Environment]::TickCount)
$mDemarrer.Invoke($ctrl, @()) | Out-Null

# on laisse le garde tourner sans jamais rafraichir la date
$sw = [Diagnostics.Stopwatch]::StartNew()
while (-not [bool]$fCoupeGarde.GetValue($ctrl) -and $sw.ElapsedMilliseconds -lt 2000) {
    Start-Sleep -Milliseconds 20
}
$sw.Stop()

if ([bool]$fCoupeGarde.GetValue($ctrl)) {
    Ok ("le garde a coupe apres {0} ms de silence du jeu" -f $sw.ElapsedMilliseconds)
    if ([float]$fGain.GetValue($lecteur) -eq 0.0) { Ok 'le gain du lecteur est bien tombe a zero' }
    else { Echec ("gain du lecteur : {0}, attendu 0" -f $fGain.GetValue($lecteur)) }
} else {
    Echec 'le garde n a pas coupe : la musique continuerait pendant la pause'
}

# et il ne doit PAS couper quand le jeu vit
$tC.GetMethod('Eteindre', $NP).Invoke($ctrl, @()) | Out-Null
Start-Sleep -Milliseconds 120
$ctrl2 = $tC.GetConstructors($NP)[0].Invoke(@($lecteur))
$fGain.SetValue($lecteur, [float]0.8)
$tC.GetField('_gainActuel', $NP).SetValue($ctrl2, [float]0.8)
$tC.GetField('_dernierTick', $NP).SetValue($ctrl2, [int][Environment]::TickCount)
$tC.GetMethod('DemarrerGarde', $NP).Invoke($ctrl2, @()) | Out-Null
$sw2 = [Diagnostics.Stopwatch]::StartNew()
while ($sw2.ElapsedMilliseconds -lt 700) {
    $tC.GetField('_dernierTick', $NP).SetValue($ctrl2, [int][Environment]::TickCount)
    Start-Sleep -Milliseconds 25
}
$sw2.Stop()
if (-not [bool]$tC.GetField('_coupeParLaGarde', $NP).GetValue($ctrl2)) {
    Ok 'tant que le jeu tourne, le garde ne touche a rien'
} else {
    Echec 'le garde coupe alors que le jeu tourne normalement'
}
$tC.GetMethod('Eteindre', $NP).Invoke($ctrl2, @()) | Out-Null

Write-Host ''
if ($echecs -eq 0) { Write-Host '=== FONDUS : TOUS LES TESTS PASSENT ===' -ForegroundColor Green }
else { Write-Host ("=== FONDUS : {0} ECHEC(S) ===" -f $echecs) -ForegroundColor Red; exit 1 }
