$ErrorActionPreference = 'Continue'
$W    = Split-Path -Parent $PSScriptRoot
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'build\trouve-jeu.ps1')
$game = Trouve-Jeu
$src  = Join-Path $W 'config'
$dll  = Join-Path $W 'WorldRadio.dll'
$naud = Join-Path $W 'lib\NAudio.dll'

$echecs = 0
function Echec($m) { $script:echecs++; Write-Output ("  ECHEC : " + $m) }
function Ok($m)    { Write-Output ("  ok    : " + $m) }

# NAudio et SHVDN AVANT tout GetTypes() : sinon Lecteur ne se charge pas, il
# est silencieusement exclu de l'analyse, et le garde-fou passe a vide.
[void][Reflection.Assembly]::LoadFrom($naud)
[void][Reflection.Assembly]::LoadFrom((Join-Path $game 'ScriptHookVDotNet3.dll'))
$a = [Reflection.Assembly]::LoadFrom($dll)
$module = $a.ManifestModule
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance,DeclaredOnly'

# ============================================================================
#  1. Garde-fou : la DLL ne doit toucher NI le volume du peripherique Windows
#     NI celui de la sortie audio. C'est le bug qui a mis le casque a 2 %.
# ============================================================================
Write-Output ''
Write-Output '=== 1. garde-fou volume ==='

$interdits = @('AudioEndpointVolume','SimpleAudioVolume','MasterVolumeLevelScalar','SetWaveOutVolume')
$sorties = @('WasapiOut','WaveOutEvent','WaveOut','DirectSoundOut','IWavePlayer','AsioOut')

$types = $null
try { $types = $a.GetTypes() }
catch [Reflection.ReflectionTypeLoadException] {
  $types = $_.Exception.Types | ? { $_ }
  $raison = ($_.Exception.LoaderExceptions | Select-Object -First 1).Message
  Echec ('des types ne se chargent pas, l analyse serait incomplete : ' + $raison)
}
foreach ($attendu in 'WorldRadio.Lecteur','WorldRadio.Config',
                     'WorldRadio.Menu','WorldRadio.Selecteur','WorldRadio.Apercu','WorldRadio.Natif','WorldRadio.ControleurAudio') {
  if (-not ($types | Where-Object { $_.FullName -eq $attendu })) {
    Echec ('type absent de l analyse : ' + $attendu)
  }
}

$trouves = @()
foreach ($t in $types) {
  $membres = @()
  try { $membres = @($t.GetMethods($flags)) + @($t.GetConstructors($flags)) } catch { continue }
  foreach ($m in $membres) {
    $body = $null; try { $body = $m.GetMethodBody() } catch {}
    if (-not $body) { continue }
    $il = $body.GetILAsByteArray(); if (-not $il) { continue }
    $i = 0
    while ($i -lt $il.Length) {
      if ($il[$i] -eq 0x28 -or $il[$i] -eq 0x6F) {
        if ($i + 4 -lt $il.Length) {
          try {
            $mi = $module.ResolveMember([BitConverter]::ToInt32($il, $i + 1))
            $dt = if ($mi.DeclaringType) { $mi.DeclaringType.Name } else { '?' }
            $plein = $dt + '::' + $mi.Name
            foreach ($mot in $interdits) {
              if ($plein -like ('*' + $mot + '*')) { $trouves += ($t.Name + '.' + $m.Name + ' -> ' + $plein) }
            }
            if ($mi.Name -eq 'set_Volume' -and $sorties -contains $dt) {
              $trouves += ($t.Name + '.' + $m.Name + ' -> ' + $plein)
            }
          } catch {}
        }
        $i += 5
      } else { $i++ }
    }
  }
}
if ($trouves.Count -gt 0) {
  Echec 'la DLL touche au volume du systeme ou de la sortie audio :'
  $trouves | Select-Object -Unique | ForEach-Object { Write-Output ('          ' + $_) }
} else { Ok 'aucun acces au volume du peripherique ni de la sortie audio' }

$gain = $false
foreach ($t in $types) { foreach ($f in $t.GetFields($flags)) {
  if ($f.FieldType.Name -eq 'VolumeSampleProvider') { $gain = $true } } }
if ($gain) { Ok 'le gain est un VolumeSampleProvider, dans la chaine audio' }
else { Echec 'VolumeSampleProvider absent : le gain ne multiplierait aucun echantillon' }

# ============================================================================
#  2. Configuration : stations, pays, et existence reelle de chaque image
# ============================================================================
Write-Output ''
Write-Output '=== 2. configuration ==='

$tmp = Join-Path $env:TEMP ('rl_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp -Force | Out-Null
Copy-Item (Join-Path $src 'WorldRadio.ini') $tmp -Force
# les images sont cherchees dans <dossier>\WorldRadio
$sousDossier = Join-Path $tmp 'WorldRadio'
New-Item -ItemType Directory -Path $sousDossier -Force | Out-Null
Copy-Item (Join-Path $W 'assets\icons\*.png') $sousDossier -Force

$tCfg = $a.GetType('WorldRadio.Config')
$cfg = $tCfg.GetMethod('Charger', [Reflection.BindingFlags]'NonPublic,Static').Invoke($null, @([string]$tmp))
if (-not $cfg) { Echec 'Config.Charger n a rien renvoye' }

function Champ($nom) { $tCfg.GetField($nom, [Reflection.BindingFlags]'Public,Instance').GetValue($cfg) }

$stations = Champ 'Stations'
$pays     = Champ 'Pays'
$tSt = $a.GetType('WorldRadio.Station')
$tPa = $a.GetType('WorldRadio.Pays')

if ($stations.Count -eq 17) { Ok '17 stations lues' } else { Echec ('stations : ' + $stations.Count + ' au lieu de 17') }
if ($pays.Count -eq 5)      { Ok '5 pays lus' }      else { Echec ('pays : ' + $pays.Count + ' au lieu de 5') }

$attendu = @{ 'FRANCE'=3; 'SUISSE'=3; 'PORTUGAL'=4; 'ESPAGNE'=3 }
$ordre = @('FRANCE','SUISSE','PORTUGAL','ESPAGNE')
for ($i = 0; $i -lt $pays.Count; $i++) {
  $nom = $tPa.GetField('Nom').GetValue($pays[$i])
  $dr  = $tPa.GetField('Drapeau').GetValue($pays[$i])
  $st  = $tPa.GetField('Stations').GetValue($pays[$i])

  if ($i -lt $ordre.Count -and $nom -ne $ordre[$i]) { Echec ('ordre des pays : ' + $nom + ' en position ' + $i) }
  if ($attendu.ContainsKey($nom) -and $st.Count -ne $attendu[$nom]) {
    Echec ($nom + ' : ' + $st.Count + ' stations au lieu de ' + $attendu[$nom])
  } else { Ok ($nom + ' : ' + $st.Count + ' stations, drapeau ' + $dr) }

  # le drapeau declare doit exister
  if ($dr -and -not (Test-Path (Join-Path $sousDossier $dr))) { Echec ('drapeau manquant : ' + $dr) }
}

# chaque station doit avoir une image presente et une URL directe
$manquantes = 0
$hls = 0
foreach ($s in $stations) {
  $ic = $tSt.GetField('Icone').GetValue($s)
  $ur = $tSt.GetField('Url').GetValue($s)
  if ($ic -and -not (Test-Path (Join-Path $sousDossier $ic))) {
    Echec ('image manquante : ' + $ic + ' (' + $tSt.GetField('Nom').GetValue($s) + ')')
    $manquantes++
  }
  if ($ur -match '\.m3u8|\.m3u$|\.pls$') { Echec ('flux non lisible : ' + $ur); $hls++ }
}
if ($manquantes -eq 0) { Ok 'toutes les images declarees existent' }
if ($hls -eq 0) { Ok 'aucun flux en liste de lecture' }

$v = Champ 'Volume'
if ([math]::Abs($v - 1.00) -lt 0.001) { Ok 'volume 1.00 (proportion du volume musique de GTA)' }
else { Echec ('volume : ' + $v) }
$sa = Champ 'SecondesApercu'
if ([math]::Abs($sa - 5.0) -lt 0.001) { Ok 'apercu 5 s' } else { Echec ('apercu : ' + $sa) }
if ((Champ 'Apercu').ToString() -eq 'Auto') { Ok 'mode apercu = Auto' }
else { Echec ('mode apercu : ' + (Champ 'Apercu')) }
foreach ($n in 'CouperRadioJeu','SuivreReglagesJeu') {
  if ((Champ $n) -eq $true) { Ok ($n + ' = true') } else { Echec ($n + ' = ' + (Champ $n)) }
}

# ============================================================================
#  3. Lecture reelle : une station par pays, et le volume Windows ne bouge pas
# ============================================================================
Write-Output ''
Write-Output '=== 3. lecture des flux ==='

$en = New-Object NAudio.CoreAudioApi.MMDeviceEnumerator
$dev = $en.GetDefaultAudioEndpoint([NAudio.CoreAudioApi.DataFlow]::Render, [NAudio.CoreAudioApi.Role]::Multimedia)
$volAvant = $dev.AudioEndpointVolume.MasterVolumeLevelScalar
Write-Output ('  volume Windows avant : ' + [math]::Round($volAvant * 100, 1) + ' %')

$tLec = $a.GetType('WorldRadio.Lecteur')
$lecteur = [Activator]::CreateInstance($tLec, $true)
$mJouer   = $tLec.GetMethod('Jouer',   [Reflection.BindingFlags]'NonPublic,Instance')
$mGain    = $tLec.GetMethod('DefinirGain', [Reflection.BindingFlags]'NonPublic,Instance')
$mArreter = $tLec.GetMethod('Arreter', [Reflection.BindingFlags]'NonPublic,Instance')
$chSortie = $tLec.GetField('_sortie', [Reflection.BindingFlags]'NonPublic,Instance')

foreach ($p in $pays) {
  $st = $tPa.GetField('Stations').GetValue($p)
  if ($st.Count -eq 0) { continue }
  $s = $st[0]
  $nom = $tSt.GetField('Nom').GetValue($s)
  $url = $tSt.GetField('Url').GetValue($s)

  $mGain.Invoke($lecteur, @([float]0.2))
  $mJouer.Invoke($lecteur, @($url))
  # amorce genereuse : certains flux mettent plusieurs secondes a demarrer
  Start-Sleep -Seconds 2
  $p1 = 0; $p2 = 0
  $sortie = $chSortie.GetValue($lecteur)
  if ($sortie) { $p1 = $sortie.GetPosition() }
  Start-Sleep -Seconds 7
  $sortie = $chSortie.GetValue($lecteur)
  if ($sortie) { $p2 = $sortie.GetPosition() }

  if ($sortie -and $p2 -gt $p1) {
    Ok ($nom + ' : lecture en cours')
    # on exerce le gain, c'est la ou le bug frappait
    $mGain.Invoke($lecteur, @([float]0.1))
    $mGain.Invoke($lecteur, @([float]0.0))
    Start-Sleep -Milliseconds 300
    $mGain.Invoke($lecteur, @([float]0.2))
  } else {
    Echec ($nom + ' : pas de lecture (pos ' + $p1 + ' -> ' + $p2 + ')')
  }
  $mArreter.Invoke($lecteur, @())
  Start-Sleep -Milliseconds 700
}

$volApres = $dev.AudioEndpointVolume.MasterVolumeLevelScalar
Write-Output ('  volume Windows apres : ' + [math]::Round($volApres * 100, 1) + ' %')
if ([math]::Abs($volApres - $volAvant) -lt 0.001) { Ok 'le volume Windows n a pas bouge' }
else { Echec ('le volume Windows a change : ' + [math]::Round($volAvant*100,1) + ' -> ' + [math]::Round($volApres*100,1)) }

# ============================================================================
#  4. Decodage des metadonnees ICY
# ============================================================================
Write-Output ''
Write-Output '=== 4. titres en cours ==='

$tMeta = $a.GetType('WorldRadio.Metadonnees')
$mExtraire = $tMeta.GetMethod('ExtraireStreamTitle', [Reflection.BindingFlags]'NonPublic,Static')
$mDecouper = $tMeta.GetMethod('Decouper', [Reflection.BindingFlags]'NonPublic,Static')
$mDecoder  = $tMeta.GetMethod('Decoder', [Reflection.BindingFlags]'NonPublic,Static')

function Decode($brut) {
  $t = $mExtraire.Invoke($null, @($brut))
  if ($null -eq $t) { return @($null, $null) }
  $pp = [object[]]@($t, $null, $null)
  $mDecouper.Invoke($null, $pp) | Out-Null
  return @($pp[1], $pp[2])
}

$r = Decode "StreamTitle='VIZINHOS - POR DO SOL';"
if ($r[0] -eq 'VIZINHOS' -and $r[1] -eq 'POR DO SOL') { Ok 'format artiste - titre' }
else { Echec ('artiste-titre : [' + $r[0] + '] [' + $r[1] + ']') }

$xml = "StreamTitle='<?xml version=""1.0""?><RadioInfo><Table>" +
       "<DB_DALET_ARTIST_NAME>Lon3r Johny, Plutonio</DB_DALET_ARTIST_NAME>" +
       "<DB_DALET_TITLE_NAME>25 de Abril</DB_DALET_TITLE_NAME></Table></RadioInfo>';StreamUrl='x';"
$r = Decode $xml
if ($r[0] -eq 'Lon3r Johny, Plutonio' -and $r[1] -eq '25 de Abril') { Ok 'format XML Dalet (Comercial, M80, Cidade)' }
else { Echec ('XML : [' + $r[0] + '] [' + $r[1] + ']') }

$r = Decode "StreamTitle='<DB_DALET_ARTIST_NAME></DB_DALET_ARTIST_NAME><DB_DALET_TITLE_NAME></DB_DALET_TITLE_NAME><SHOW_NAME>Maria Morango</SHOW_NAME>';"
if ($r[1] -eq 'Maria Morango') { Ok 'repli sur le nom de l emission' } else { Echec ('repli : [' + $r[1] + ']') }

$r = Decode "StreamTitle='Journal de 12h';"
if ($r[0] -eq '' -and $r[1] -eq 'Journal de 12h') { Ok 'titre sans artiste' } else { Echec 'titre simple' }

$r = Decode "rien du tout"
if ($null -eq $r[0]) { Ok 'flux sans StreamTitle : ignore proprement' } else { Echec 'StreamTitle absent mal gere' }

# accents : verifie sur les octets, la console les mangerait a l'affichage
$attenduChars = @(77,233,108,111,32,68,233,99,97,108,233)
$att = -join ($attenduChars | ForEach-Object { [char]$_ })
$o8 = [Text.Encoding]::UTF8.GetBytes($att)
if ($mDecoder.Invoke($null, @($o8, $o8.Length)) -ceq $att) { Ok 'accents : UTF-8' }
else { Echec 'accents UTF-8' }
$o1 = [Text.Encoding]::GetEncoding(28591).GetBytes($att)
if ($mDecoder.Invoke($null, @($o1, $o1.Length)) -ceq $att) { Ok 'accents : Latin-1' }
else { Echec 'accents Latin-1' }

# ============================================================================
#  5. Sources de metadonnees declarees, et appel Triton reel
# ============================================================================
Write-Output ''
Write-Output '=== 5. sources de metadonnees ==='

$compte = @{ 'Icy'=0; 'Triton'=0; 'Aucune'=0 }
foreach ($s in $stations) {
  $src = $tSt.GetField('Source').GetValue($s).ToString()
  if ($compte.ContainsKey($src)) { $compte[$src]++ }
  $nom = $tSt.GetField('Nom').GetValue($s)
  $id  = $tSt.GetField('IdMeta').GetValue($s)
  # une station Triton sans identifiant de mount ne renverrait jamais rien
  if ($src -eq 'Triton' -and [string]::IsNullOrEmpty($id)) { Echec ($nom + ' : Triton sans MetadataId') }
}
if ($compte['Triton'] -eq 3) { Ok '3 stations en Triton' } else { Echec ('Triton : ' + $compte['Triton'] + ' au lieu de 3') }
if ($compte['Aucune'] -eq 3) { Ok '3 stations sans source (NRJ, RFM France, KROQ)' } else { Echec ('Aucune : ' + $compte['Aucune'] + ' au lieu de 3') }
if ($compte['Icy'] -eq 11)   { Ok '11 stations en ICY' } else { Echec ('Icy : ' + $compte['Icy'] + ' au lieu de 11') }

# extraction d une propriete du XML Triton
$mProp = $tMeta.GetMethod('Propriete', [Reflection.BindingFlags]'NonPublic,Static')
$faux = '<property name="cue_title"><![CDATA[Love yourself]]></property>' +
        '<property name="track_artist_name"><![CDATA[Justin Bieber]]></property>' +
        '<property name="track_cover_url"><![CDATA[https://exemple/x.jpg]]></property>'
if ($mProp.Invoke($null, @($faux, 'cue_title')) -eq 'Love yourself') { Ok 'Triton : titre extrait' }
else { Echec 'Triton : extraction du titre' }
if ($mProp.Invoke($null, @($faux, 'track_artist_name')) -eq 'Justin Bieber') { Ok 'Triton : artiste extrait' }
else { Echec 'Triton : extraction de l artiste' }
if ($mProp.Invoke($null, @($faux, 'track_cover_url')) -eq 'https://exemple/x.jpg') { Ok 'Triton : pochette extraite' }
else { Echec 'Triton : extraction de la pochette' }
if ($mProp.Invoke($null, @($faux, 'inexistant')) -eq '') { Ok 'Triton : propriete absente geree' }
else { Echec 'Triton : propriete absente mal geree' }

# appel reel sur les trois mounts declares
Write-Output ''
foreach ($m in 'LOS40','CADENADIAL','RFM') {
  try {
    $wc = New-Object Net.WebClient
    $wc.Headers.Add('User-Agent','WorldRadio/1.0')
    $x = $wc.DownloadString('https://np.tritondigital.com/public/nowplaying?mountName=' + $m + '&numberToFetch=1&eventType=track')
    $ar = $mProp.Invoke($null, @($x, 'track_artist_name'))
    $ti = $mProp.Invoke($null, @($x, 'cue_title'))
    $co = $mProp.Invoke($null, @($x, 'track_cover_url'))
    if ($ti) {
      Ok ($m + ' : ' + $ar + ' - ' + $ti + $(if ($co) { '   [pochette]' } else { '' }))
    } else { Echec ($m + ' : aucun titre renvoye') }
  } catch { Echec ($m + ' : ' + $_.Exception.Message) }
}

# ============================================================================
#  6. Volume : conversion du curseur, et autorite unique
# ============================================================================
Write-Output ''
Write-Output '=== 6. volume ==='

$tNat = $a.GetType('WorldRadio.Natif')
$mNorm = $tNat.GetMethod('Normaliser', [Reflection.BindingFlags]'NonPublic,Static')

# plage 0 a 10, celle documentee
$cas = @{ 0 = 0.0; 3 = 0.3; 5 = 0.5; 8 = 0.8; 10 = 1.0 }
$okPlage = $true
foreach ($k in $cas.Keys) {
  $r = $mNorm.Invoke($null, @([int]$k))
  if ([math]::Abs($r - $cas[$k]) -gt 0.001) { Echec ('curseur ' + $k + ' -> ' + $r + ' au lieu de ' + $cas[$k]); $okPlage = $false }
}
if ($okPlage) { Ok 'curseur 0..10 converti correctement (0 / 25 / 50 / 75 / 100 %)' }

# plage 0 a 100, au cas ou Enhanced l exposerait ainsi
if ([math]::Abs($mNorm.Invoke($null, @([int]50)) - 0.5) -lt 0.001) { Ok 'plage 0..100 geree aussi' }
else { Echec 'plage 0..100 mal geree' }

# valeurs hors plage : on ne doit pas inventer un volume
if ($mNorm.Invoke($null, @([int]-1)) -lt 0) { Ok 'valeur illisible signalee, pas devinee' }
else { Echec 'une valeur negative aurait du etre rejetee' }
if ($mNorm.Invoke($null, @([int]500)) -lt 0) { Ok 'valeur aberrante rejetee' }
else { Echec 'une valeur aberrante aurait du etre rejetee' }

# zero exact : aucun residu audible
if ($mNorm.Invoke($null, @([int]0)) -eq 0.0) { Ok 'curseur a 0 donne exactement 0' }
else { Echec 'curseur a 0 ne donne pas exactement 0' }

# une SEULE classe doit ecrire dans l attenuateur
$ecrivains = @()
foreach ($t in $types) {
  foreach ($m in @($t.GetMethods($flags))) {
    $body = $null; try { $body = $m.GetMethodBody() } catch {}
    if (-not $body) { continue }
    $il = $body.GetILAsByteArray(); if (-not $il) { continue }
    $i = 0
    while ($i -lt $il.Length) {
      if ($il[$i] -eq 0x28 -or $il[$i] -eq 0x6F) {
        if ($i + 4 -lt $il.Length) {
          try {
            $mi = $module.ResolveMember([BitConverter]::ToInt32($il, $i + 1))
            if ($mi.Name -eq 'set_Volume' -and $mi.DeclaringType -and
                $mi.DeclaringType.Name -eq 'VolumeSampleProvider') { $ecrivains += $t.Name }
          } catch {}
        }
        $i += 5
      } else { $i++ }
    }
  }
}
# @() force un tableau : sans cela un resultat unique redevient une chaine,
# et l'indexer renverrait son premier caractere
$ecrivains = @($ecrivains | Sort-Object -Unique)
if ($ecrivains.Count -eq 1 -and $ecrivains[0] -eq 'Lecteur') {
  Ok 'un seul ecrivain du gain : Lecteur, pilote par ControleurAudio'
} else {
  Echec ('plusieurs classes ecrivent le gain : ' + ($ecrivains -join ', '))
}

# ============================================================================
#  7. Code retire : R3, et les textures issues d'un telechargement
# ============================================================================
Write-Output ''
Write-Output '=== 7. code retire ==='

# R3 et toute la lecture manette ne doivent plus exister, meme desactives
$chaines = @()
$nbChaines = 0
for ($tok = 0x70000001; $tok -lt 0x70000001 + 4000; $tok++) {
  try { $s = $module.ResolveString($tok); $chaines += $s; $nbChaines++ } catch { }
}
$interditsR3 = @('PadR3','PadL3','XInputGetState','HidD_GetAttributes','GamepadButton','LearnGamepadButton')
$restants = @()
foreach ($mot in $interditsR3) {
  if ($chaines | Where-Object { $_ -and $_.Contains($mot) }) { $restants += $mot }
}
if ($restants.Count -eq 0) { Ok ("aucune trace de R3 ni de lecture manette (" + $nbChaines + ' chaines analysees)') }
else { Echec ('code manette encore present : ' + ($restants -join ', ')) }

foreach ($interdit in 'WorldRadio.Entrees') {
  if ($types | Where-Object { $_.FullName -eq $interdit }) { Echec ('type non supprime : ' + $interdit) }
  else { Ok ('type bien supprime : ' + $interdit) }
}

# aucune texture ne doit etre creee depuis un fichier telecharge
if ($chaines | Where-Object { $_ -and $_.Contains('cache') }) {
  Echec 'le cache de pochettes subsiste : risque de crash DirectX'
} else { Ok 'plus de cache de pochettes : le crash DirectX ne peut plus survenir' }

# ============================================================================
#  8. Non-regression : changements rapides, un seul flux a la fin
# ----------------------------------------------------------------------------
#  Ce bug est apparu deux fois. Survoler les stations lance une connexion par
#  station ; celle qui perd la course appelait quand meme Play() sans que
#  personne ne garde sa reference, donc hors de portee du reglage de gain.
#  Resultat en jeu : deux radios simultanees, sourdes au volume et a la pause.
# ============================================================================
Write-Output ''
Write-Output '=== 8. changements rapides ==='

$lecteur2 = [Activator]::CreateInstance($tLec, $true)
$mGain2   = $tLec.GetMethod('DefinirGain', [Reflection.BindingFlags]'NonPublic,Instance')
$mJouer2  = $tLec.GetMethod('Jouer', [Reflection.BindingFlags]'NonPublic,Instance')
$mArr2    = $tLec.GetMethod('Arreter', [Reflection.BindingFlags]'NonPublic,Instance')
$chS2     = $tLec.GetField('_sortie', [Reflection.BindingFlags]'NonPublic,Instance')
$chUrl    = $tLec.GetField('_urlVoulue', [Reflection.BindingFlags]'NonPublic,Instance')
$chGen    = $tLec.GetField('_generation', [Reflection.BindingFlags]'NonPublic,Instance')

$mGain2.Invoke($lecteur2, @([float]0.0))     # muet : on mesure, on n ecoute pas

# on enchaine cinq stations sans laisser le temps de se connecter
$suite = @()
foreach ($s in $stations) { $suite += $tSt.GetField('Url').GetValue($s) }
$cinq = $suite | Select-Object -First 5
foreach ($u in $cinq) {
  $mJouer2.Invoke($lecteur2, @([string]$u))
  Start-Sleep -Milliseconds 220
}
$derniere = [string]$cinq[$cinq.Count - 1]

# le temps que la derniere connexion aboutisse
Start-Sleep -Seconds 12

$urlRetenue = $chUrl.GetValue($lecteur2)
if ($urlRetenue -eq $derniere) { Ok 'la derniere demande est bien celle retenue' }
else { Echec ('url retenue : ' + $urlRetenue) }

$sortie2 = $chS2.GetValue($lecteur2)
if ($sortie2 -and $sortie2.PlaybackState.ToString() -eq 'Playing') {
  Ok 'un flux joue apres cinq changements rapides'
} else {
  Echec ('aucun flux actif apres les changements : ' + $(if ($sortie2) { $sortie2.PlaybackState } else { 'sortie nulle' }))
}

# la generation doit avoir suivi chaque demande : c est le garde-fou anti-orphelin
$gen = $chGen.GetValue($lecteur2)
if ($gen -ge $cinq.Count) { Ok ('compteur de generation a ' + $gen + ', les demandes perimees sont ecartees') }
else { Echec ('generation ' + $gen + ' pour ' + $cinq.Count + ' demandes') }

# arret immediatement suivi d un demarrage : la fermeture differee ne doit pas
# tuer le flux neuf (c est exactement ce qui faisait echouer LOS40)
$mArr2.Invoke($lecteur2, @())
$mJouer2.Invoke($lecteur2, @([string]$cinq[0]))
Start-Sleep -Seconds 12

$sortie3 = $chS2.GetValue($lecteur2)
if ($sortie3 -and $sortie3.PlaybackState.ToString() -eq 'Playing') {
  Ok 'arret puis demarrage immediat : le flux neuf survit'
} else {
  Echec 'la fermeture differee a tue le flux neuf'
}
$mArr2.Invoke($lecteur2, @())
Start-Sleep -Milliseconds 800

try { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue } catch {}

Write-Output ''
if ($echecs -eq 0) { Write-Output '=== TOUS LES TESTS PASSENT ===' }
else { Write-Output ('=== ' + $echecs + ' ECHEC(S) ===') ; exit 1 }
