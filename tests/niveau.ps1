# ============================================================================
#  Niveau des stations, et limiteur
# ----------------------------------------------------------------------------
#  Les radios ne sont pas masterisees au meme niveau : mesure faite, 11 LU
#  separent Skyrock de HOT 97. Chaque station porte donc une correction dans
#  le .ini, et un limiteur empeche les stations remontees de saturer.
#
#  Ce test verifie les trois maillons : la lecture des corrections, le
#  comportement du limiteur, et leur coherence avec la cible.
# ============================================================================

$ErrorActionPreference = 'Stop'
$W = Split-Path -Parent $PSScriptRoot

$echecs = 0
function Ok($m)    { Write-Host ('  ok    : ' + $m) -ForegroundColor Green }
function Echec($m) { Write-Host ('  ECHEC : ' + $m) -ForegroundColor Red; $script:echecs++ }

[void][Reflection.Assembly]::LoadFrom((Join-Path $W 'lib\NAudio.dll'))
$a  = [Reflection.Assembly]::LoadFrom((Join-Path $W 'WorldRadio.dll'))
$NP = [Reflection.BindingFlags]'NonPublic,Public,Instance,Static'
$inv = [Globalization.CultureInfo]::InvariantCulture

Write-Host ''
Write-Host '=== niveau des stations ===' -ForegroundColor Cyan

# --- 1. le mod lit-il les corrections ? -------------------------------------
$tCfg = $a.GetType('WorldRadio.Config')
$cfg = $tCfg.GetMethod('Charger', $NP).Invoke($null, @([string](Join-Path $W 'config')))
$stations = $tCfg.GetField('Stations', $NP).GetValue($cfg)

$brut = @{}
$nom = $null
foreach ($l in Get-Content (Join-Path $W 'config\WorldRadio.ini')) {
    if ($l -match '^Name=(.+)$') { $nom = $Matches[1] }
    if ($l -match '^Gain=(.+)$' -and $nom) { $brut[$nom] = [double]::Parse($Matches[1], $inv) }
}

$ecarts = @()
foreach ($s in $stations) {
    if (-not $brut.ContainsKey($s.Nom)) { $ecarts += ($s.Nom + ' : aucune correction'); continue }
    if ([Math]::Abs($s.GainDb - $brut[$s.Nom]) -gt 0.001) { $ecarts += ('{0} : lu {1}, ecrit {2}' -f $s.Nom, $s.GainDb, $brut[$s.Nom]) }
}
if ($ecarts.Count -eq 0) { Ok ("les {0} corrections du .ini sont lues a l identique" -f $stations.Count) }
else { Echec ('corrections mal lues : ' + ($ecarts -join ' | ')) }

# les decimales doivent survivre : un arrondi a l entier serait un defaut
$decimales = @($stations | Where-Object { [Math]::Abs($_.GainDb - [Math]::Round($_.GainDb)) -gt 0.001 }).Count
if ($decimales -ge 10) { Ok ("les corrections gardent leurs decimales ({0} sur {1} non entieres)" -f $decimales, $stations.Count) }
else { Echec ("corrections arrondies a l entier : seulement {0} non entieres" -f $decimales) }

# --- 2. coherence avec la cible ---------------------------------------------
$apres = @()
$mesure = $null
foreach ($l in Get-Content (Join-Path $W 'config\WorldRadio.ini')) {
    if ($l -match '^; niveau mesure : (\S+) LUFS') { $mesure = [double]::Parse($Matches[1], $inv) }
    if ($l -match '^Gain=(.+)$' -and $mesure -ne $null) {
        $apres += $mesure + [double]::Parse($Matches[1], $inv); $mesure = $null
    }
}
$st = $apres | Measure-Object -Minimum -Maximum
if (($st.Maximum - $st.Minimum) -le 0.2) {
    Ok ("apres correction, toutes les stations tiennent dans {0:N2} LU (de {1:N2} a {2:N2} LUFS)" -f ($st.Maximum - $st.Minimum), $st.Minimum, $st.Maximum)
} else {
    Echec ("apres correction, encore {0:N2} LU d ecart entre stations" -f ($st.Maximum - $st.Minimum))
}

# --- 3. le limiteur ne laisse rien depasser ---------------------------------
$tLim = $a.GetType('WorldRadio.Limiteur')
$seuil = [float]$tLim.GetField('Seuil', $NP).GetValue($null)
$ctorLim = $tLim.GetConstructors($NP)[0]

function Generateur([double]$gain) {
    $g = New-Object NAudio.Wave.SampleProviders.SignalGenerator(48000, 2)
    $g.Type = [NAudio.Wave.SampleProviders.SignalGeneratorType]::Sin
    $g.Frequency = 997
    $g.Gain = $gain
    # sans ,, PowerShell enveloppe l'objet dans un PSObject que la reflexion
    # refuse de convertir en ISampleProvider
    return ,$g
}
function Crete($source, [int]$echantillons) {
    $buf = New-Object float[] 4800
    $max = 0.0; $lus = 0
    while ($lus -lt $echantillons) {
        $n = $source.Read($buf, 0, $buf.Length)
        for ($i = 0; $i -lt $n; $i++) { $v = [Math]::Abs($buf[$i]); if ($v -gt $max) { $max = $v } }
        $lus += $n
    }
    return $max
}

# un signal 3,5 dB au-dessus de 0 dBFS
$lim = $ctorLim.Invoke([object[]]@((Generateur 1.5).psobject.BaseObject))
$c = Crete $lim 96000
if ($c -le $seuil + 1e-5) {
    Ok ("signal a +3,5 dBFS ramene a {0:N2} dBFS, rien au-dessus du seuil de {1:N2}" -f (20*[Math]::Log10($c)), (20*[Math]::Log10($seuil)))
} else {
    Echec ("le limiteur laisse passer {0:N3} pour un seuil de {1:N3}" -f $c, $seuil)
}

# un signal sous le seuil doit ressortir IDENTIQUE, echantillon pour echantillon
$limT = $ctorLim.Invoke([object[]]@((Generateur 0.5).psobject.BaseObject))
$refT = (Generateur 0.5).psobject.BaseObject
$b1 = New-Object float[] 9600; $b2 = New-Object float[] 9600
$ecartMax = 0.0
for ($k = 0; $k -lt 10; $k++) {
    [void]$limT.Read($b1, 0, 9600); [void]$refT.Read($b2, 0, 9600)
    for ($i = 0; $i -lt 9600; $i++) { $e = [Math]::Abs($b1[$i] - $b2[$i]); if ($e -gt $ecartMax) { $ecartMax = $e } }
}
if ($ecartMax -eq 0.0) { Ok 'sous le seuil, le limiteur est parfaitement transparent (ecart nul)' }
else { Echec ("sous le seuil, le limiteur altere le signal (ecart {0})" -f $ecartMax) }

# --- 4. il est bien dans la chaine, et au bon endroit -----------------------
$lect = Get-Content (Join-Path $W 'src\Lecteur.cs') -Raw
if ($lect -match 'new Limiteur\(attenuateur\)' -and $lect -match 'sortie\.Init\(garde\)') {
    Ok 'le limiteur est branche apres le gain, juste avant la sortie'
} else {
    Echec 'le limiteur n est pas dans la chaine, ou pas apres le gain'
}

# --- 5. le gain de station passe par le calcul unique -----------------------
$aud = Get-Content (Join-Path $W 'src\Audio.cs') -Raw
if ($aud -match 'float plein = _volumeJeu \* _proportion \* _gainStation;') {
    Ok 'la correction de station entre dans le calcul unique du gain'
} else {
    Echec 'la correction de station est appliquee hors du calcul unique'
}

Write-Host ''
if ($echecs -eq 0) { Write-Host '=== NIVEAU : TOUS LES TESTS PASSENT ===' -ForegroundColor Green }
else { Write-Host ("=== NIVEAU : {0} ECHEC(S) ===" -f $echecs) -ForegroundColor Red; exit 1 }
