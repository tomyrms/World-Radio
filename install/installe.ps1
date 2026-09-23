# ============================================================================
#  WORLD RADIO  -  installation
# ----------------------------------------------------------------------------
#  Lance par INSTALLER.cmd. Tout se fait ici, en une fois :
#
#    1. trouver le jeu, sans supposer ou il est ni comment son dossier
#       s'appelle ; a defaut, demander a l'utilisateur de montrer l'exe
#    2. verifier ScriptHookV et ScriptHookVDotNet
#    3. mettre de cote une ancienne version (Radio Libre) en gardant ses
#       reglages
#    4. copier le mod, deja compile
#
#  Tout est note dans WorldRadio-installation.log, a cote d'INSTALLER.cmd :
#  si quelque chose coince, c'est ce fichier qu'il faut envoyer.
#
#  Compatible Windows PowerShell 5.1, present sur tout Windows 10 et 11.
# ============================================================================

param([string]$Jeu = '', [switch]$SansPause)

$ErrorActionPreference = 'Stop'
$Ici = Split-Path -Parent $MyInvocation.MyCommand.Path
$Journal = Join-Path (Split-Path -Parent $Ici) 'WorldRadio-installation.log'

function Noter([string]$m) {
    try { Add-Content -LiteralPath $script:Journal -Value ((Get-Date -Format 'HH:mm:ss') + '  ' + $m) -Encoding UTF8 } catch { }
}
function Dire([string]$m, [string]$couleur = 'Gray') { Write-Host $m -ForegroundColor $couleur; Noter $m }
function Titre([string]$m) { Write-Host ''; Dire ('  ' + $m) 'Cyan' }
function Bien([string]$m)  { Dire ('   [ok]  ' + $m) 'Green' }
function Info([string]$m)  { Dire ('   [.]   ' + $m) 'Gray' }
function Alerte([string]$m){ Dire ('   [!]   ' + $m) 'Yellow' }
function Grave([string]$m) { Dire ('   [X]   ' + $m) 'Red' }
function Terminer([int]$code) {
    Write-Host ''
    Write-Host ('  Journal complet : ' + $script:Journal) -ForegroundColor DarkGray
    if (-not $SansPause) { Write-Host ''; Read-Host '  Appuie sur Entree pour fermer' | Out-Null }
    exit $code
}

try { Set-Content -LiteralPath $Journal -Value ('World Radio - installation du ' + (Get-Date -Format 'yyyy-MM-dd HH:mm')) -Encoding UTF8 } catch { }
Noter ('Windows ' + [Environment]::OSVersion.Version + '   PowerShell ' + $PSVersionTable.PSVersion)
Noter ('dossier de l installeur : ' + $Ici)

Write-Host ''
Write-Host '  ============================================================' -ForegroundColor White
Write-Host '   WORLD RADIO  -  radios internet pour GTA V Enhanced' -ForegroundColor White
Write-Host '  ============================================================' -ForegroundColor White

try {
    # Un zip telecharge marque chaque fichier comme venu d'Internet. .NET peut
    # alors refuser de charger le DLL dans le jeu, sans message clair : on
    # retire la marque tout de suite, sur tout le contenu du zip.
    Get-ChildItem -LiteralPath (Split-Path -Parent $Ici) -Recurse -File -ErrorAction SilentlyContinue |
        Unblock-File -ErrorAction SilentlyContinue

    . (Join-Path $Ici 'trouve-jeu.ps1')

    # ========================================================================
    #  1. Le jeu
    # ========================================================================
    Titre '1. Recherche de GTA V Enhanced'

    $dossierJeu = $null
    if ($Jeu) {
        if (Test-Path -LiteralPath (Join-Path $Jeu 'GTA5_Enhanced.exe')) { $dossierJeu = (Resolve-Path -LiteralPath $Jeu).Path; Bien ('dossier impose : ' + $dossierJeu) }
        else { Grave ('GTA5_Enhanced.exe absent de : ' + $Jeu); Terminer 1 }
    } else {
        Info 'Epic Games, Steam, Rockstar, programmes installes...'
        $trouves = @(Trouve-Candidats -Trace { param($m) Noter ('  ' + $m) })

        if ($trouves.Count -eq 0) {
            Alerte 'le jeu n a pas ete trouve automatiquement.'
            Info 'une fenetre va s ouvrir : selectionne GTA5_Enhanced.exe'
            Info '(c est dans le dossier ou GTA V Enhanced est installe)'
            $exe = Demander-Exe
            if ($exe) {
                Noter ('choisi a la main : ' + $exe)
                if ((Split-Path -Leaf $exe) -ieq 'GTA5.exe') {
                    Grave 'c est GTA5.exe : la version LEGACY. World Radio est fait pour GTA V Enhanced.'
                    Terminer 1
                }
                $d = Split-Path -Parent $exe
                if (Test-Path -LiteralPath (Join-Path $d 'GTA5_Enhanced.exe')) { $dossierJeu = $d }
                else { Grave 'ce dossier ne contient pas GTA5_Enhanced.exe.' }
            }
            if (-not $dossierJeu) {
                Grave 'aucun dossier de jeu : rien n a ete modifie.'
                Terminer 1
            }
            Bien ('jeu : ' + $dossierJeu)
        } elseif ($trouves.Count -eq 1) {
            $dossierJeu = $trouves[0].Dossier
            Bien ('jeu trouve (' + $trouves[0].Source + ') : ' + $dossierJeu)
        } else {
            Dire '   Plusieurs installations trouvees :' 'White'
            for ($i = 0; $i -lt $trouves.Count; $i++) {
                Dire ('     {0})  {1}   ({2})' -f ($i + 1), $trouves[$i].Dossier, $trouves[$i].Source) 'White'
            }
            $n = 0
            do { $r = Read-Host '   Laquelle ? (numero)' }
            until ([int]::TryParse($r, [ref]$n) -and $n -ge 1 -and $n -le $trouves.Count)
            $dossierJeu = $trouves[$n - 1].Dossier
            Bien ('jeu choisi : ' + $dossierJeu)
        }
    }

    $ver = ''
    try { $ver = (Get-Item -LiteralPath (Join-Path $dossierJeu 'GTA5_Enhanced.exe')).VersionInfo.FileVersion } catch { }
    if ($ver) { Info ('version du jeu : ' + $ver) }

    # --- le jeu doit etre ferme : Windows verrouille les fichiers ouverts ---
    $racine = $dossierJeu.TrimEnd('\')
    function Jeu-Ouvert {
        foreach ($p in (Get-Process -Name GTA5_Enhanced -ErrorAction SilentlyContinue)) {
            $ch = $null
            try { $ch = $p.Path } catch { }
            if (-not $ch -or $ch.StartsWith($racine, [StringComparison]::OrdinalIgnoreCase)) { return $true }
        }
        return $false
    }
    if (Jeu-Ouvert) {
        Alerte 'GTA V est ouvert. Ferme-le : l installation continuera toute seule.'
        $t = 0
        while (Jeu-Ouvert) {
            Start-Sleep -Seconds 2; $t += 2
            if ($t -ge 900) { Grave 'toujours ouvert apres 15 minutes : abandon, rien n a ete modifie.'; Terminer 1 }
        }
        Bien 'jeu ferme, on continue'
        Start-Sleep -Seconds 2
    }

    # ========================================================================
    #  2. Prerequis
    # ========================================================================
    Titre '2. Prerequis'

    $scripts = Join-Path $dossierJeu 'scripts'
    $aSHV = Test-Path -LiteralPath (Join-Path $dossierJeu 'ScriptHookV.dll')
    $aSHVDN = (Test-Path -LiteralPath (Join-Path $dossierJeu 'ScriptHookVDotNet.asi')) -and
              (Test-Path -LiteralPath (Join-Path $dossierJeu 'ScriptHookVDotNet3.dll'))
    $manque = @()

    if ($aSHV) { Bien 'Script Hook V' }
    else {
        $manque += 'Script Hook V'
        Alerte 'Script Hook V absent : sans lui, aucun mod ne se lance.'
        Info  '  a telecharger ici : http://www.dev-c.com/gtav/scripthookv/'
        Info ('  puis copier son contenu dans : ' + $dossierJeu)
    }
    if ($aSHVDN) { Bien 'ScriptHookVDotNet' }
    else {
        $manque += 'ScriptHookVDotNet'
        Alerte 'ScriptHookVDotNet absent : World Radio en a besoin pour tourner.'
        Info  '  version ENHANCED, ici : https://github.com/scripthookvdotnet/scripthookvdotnet/releases'
        Info ('  puis copier son contenu dans : ' + $dossierJeu)
    }

    # ========================================================================
    #  3. Ancienne version et reglages
    # ========================================================================
    Titre '3. Installation'

    New-Item -ItemType Directory -Force -Path $scripts | Out-Null
    $sauve = Join-Path $dossierJeu ('_ancien_world_radio_' + (Get-Date -Format 'yyyy-MM-dd_HHmmss'))

    function Mettre-De-Cote([string]$nom) {
        $c = Join-Path $scripts $nom
        if (-not (Test-Path -LiteralPath $c)) { return }
        New-Item -ItemType Directory -Force -Path $sauve | Out-Null
        Move-Item -LiteralPath $c -Destination (Join-Path $sauve $nom) -Force
        Info ('mis de cote : ' + $nom)
    }

    # Radio Libre, l'ancien nom : laisser les deux ferait jouer deux radios
    $ancienIni = $null
    if (Test-Path -LiteralPath (Join-Path $scripts 'RadioLibre.ini')) { $ancienIni = Join-Path $sauve 'RadioLibre.ini' }
    foreach ($n in 'RadioLibre.dll', 'RadioLibre.ini', 'RadioLibre.log', 'RadioLibre') { Mettre-De-Cote $n }

    # reglages du joueur : releves avant de poser le nouveau .ini
    $garder = 'Volume', 'LastStation', 'MuteKey', 'VolumeUpKey', 'VolumeDownKey', 'NextStationKey',
              'PrevStationKey', 'NowPlaying', 'NowPlayingSeconds', 'InvertMenuAxis', 'AudioLog',
              'NowPlayingRaise', 'WheelSlowMotion', 'DuckDuringDialogue', 'DuckOnAmbientSpeech'
    $iniJeu = Join-Path $scripts 'WorldRadio.ini'
    $source = $null
    if (Test-Path -LiteralPath $iniJeu) { $source = $iniJeu }
    elseif ($ancienIni -and (Test-Path -LiteralPath $ancienIni)) { $source = $ancienIni; Info 'reglages repris de Radio Libre' }
    $perso = @{}
    if ($source) {
        foreach ($l in (Get-Content -LiteralPath $source)) {
            $t = $l.Trim()
            if ($t.StartsWith(';') -or -not $t.Contains('=')) { continue }
            $k = $t.Substring(0, $t.IndexOf('=')).Trim()
            if ($garder -contains $k) { $perso[$k] = $t.Substring($t.IndexOf('=') + 1).Trim() }
        }
    }
    if (Test-Path -LiteralPath $iniJeu) {
        New-Item -ItemType Directory -Force -Path $sauve | Out-Null
        Copy-Item -LiteralPath $iniJeu -Destination (Join-Path $sauve 'WorldRadio.ini') -Force
    }

    # ========================================================================
    #  4. Copie
    # ========================================================================
    foreach ($f in 'WorldRadio.dll', 'WorldRadio.ini') {
        Copy-Item -LiteralPath (Join-Path $Ici $f) -Destination (Join-Path $scripts $f) -Force
        Bien ('{0,-18} {1,9:N0} o' -f $f, (Get-Item -LiteralPath (Join-Path $Ici $f)).Length)
    }

    # NAudio peut deja etre la, pose par un autre mod : on ne le remplace que
    # s'il differe, et on garde l'ancien
    $naudio = Join-Path $scripts 'NAudio.dll'
    $notre = Join-Path $Ici 'NAudio.dll'
    if ((Test-Path -LiteralPath $naudio) -and ((Get-Item -LiteralPath $naudio).Length -eq (Get-Item -LiteralPath $notre).Length)) {
        Info 'NAudio.dll deja present et identique'
    } else {
        if (Test-Path -LiteralPath $naudio) {
            New-Item -ItemType Directory -Force -Path $sauve | Out-Null
            Copy-Item -LiteralPath $naudio -Destination (Join-Path $sauve 'NAudio.dll') -Force
            Alerte 'NAudio.dll different remplace (l ancien est garde de cote)'
        }
        Copy-Item -LiteralPath $notre -Destination $naudio -Force
        Bien ('{0,-18} {1,9:N0} o' -f 'NAudio.dll', (Get-Item -LiteralPath $notre).Length)
    }

    $icones = Join-Path $scripts 'WorldRadio'
    New-Item -ItemType Directory -Force -Path $icones | Out-Null
    $nbImages = 0
    foreach ($png in (Get-ChildItem -LiteralPath (Join-Path $Ici 'WorldRadio') -Filter '*.png')) {
        Copy-Item -LiteralPath $png.FullName -Destination (Join-Path $icones $png.Name) -Force
        $nbImages++
    }
    Bien ("$nbImages images (logos, drapeaux)")

    # --- on rend les reglages -------------------------------------------------
    if ($perso.Count -gt 0) {
        $lignes = New-Object System.Collections.Generic.List[string]
        foreach ($l in (Get-Content -LiteralPath $iniJeu)) { $lignes.Add($l) }
        $vus = @{}
        for ($i = 0; $i -lt $lignes.Count; $i++) {
            $t = $lignes[$i].Trim()
            if ($t.StartsWith(';') -or -not $t.Contains('=')) { continue }
            $k = $t.Substring(0, $t.IndexOf('=')).Trim()
            if ($perso.ContainsKey($k)) { $lignes[$i] = $k + '=' + $perso[$k]; $vus[$k] = $true }
        }
        $general = -1
        for ($i = 0; $i -lt $lignes.Count; $i++) { if ($lignes[$i].Trim() -eq '[General]') { $general = $i; break } }
        foreach ($k in $perso.Keys) {
            if ($vus.ContainsKey($k)) { continue }
            if ($general -ge 0) { $lignes.Insert($general + 1, $k + '=' + $perso[$k]) } else { $lignes.Add($k + '=' + $perso[$k]) }
        }
        Set-Content -LiteralPath $iniJeu -Value $lignes -Encoding UTF8
        Info ("{0} reglage(s) personnel(s) conserve(s)" -f $perso.Count)
    }
    if (Test-Path -LiteralPath $sauve) { Info ('ancienne version gardee dans ' + (Split-Path -Leaf $sauve)) }

    # la copie conserve la marque "venu d'Internet" : on la retire aussi ici
    foreach ($f in 'WorldRadio.dll', 'WorldRadio.ini', 'NAudio.dll') {
        Unblock-File -LiteralPath (Join-Path $scripts $f) -ErrorAction SilentlyContinue
    }
    Get-ChildItem -LiteralPath $icones -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue

    # ========================================================================
    #  5. Controle
    # ========================================================================
    Titre '4. Controle'
    $souci = $false
    foreach ($f in 'WorldRadio.dll', 'WorldRadio.ini', 'NAudio.dll') {
        if (Test-Path -LiteralPath (Join-Path $scripts $f)) { Bien $f } else { Grave ($f + ' manquant'); $souci = $true }
    }
    $n = @(Get-ChildItem -LiteralPath $icones -Filter '*.png' -ErrorAction SilentlyContinue).Count
    if ($n -ge 24) { Bien "$n images" } else { Grave "images : $n sur 24"; $souci = $true }

    Write-Host ''
    if ($souci) {
        Grave 'installation incomplete.'
        Terminer 1
    } elseif ($manque.Count -gt 0) {
        Alerte ('World Radio est installe, mais il manque : ' + ($manque -join ', ') + '.')
        Alerte 'Installe-le, et la radio fonctionnera au prochain lancement.'
    } else {
        Dire '  Installation terminee. Lance le jeu et monte dans une voiture.' 'Green'
    }

    Write-Host ''
    Write-Host '  ---- en jeu, au volant ----' -ForegroundColor White
    Write-Host '   Touche radio, appui BREF      eteint / rallume la derniere station'
    Write-Host '   Touche radio, MAINTENUE       ouvre la roue des stations'
    Write-Host '     (Q au clavier, croix directionnelle gauche a la manette)'
    Write-Host '     stick droit                 vise une station'
    Write-Host '     tout en haut                RADIO OFF'
    Write-Host '     R1 / RB                     pays suivant'
    Write-Host '   F10 / F9                      station suivante / precedente'
    Write-Host '   NumPad9 / NumPad3             volume'
    Noter 'fin : succes'
    Terminer 0
}
catch {
    Grave ('erreur inattendue : ' + $_.Exception.Message)
    Noter ('  a la ligne ' + $_.InvocationInfo.ScriptLineNumber + ' : ' + $_.InvocationInfo.Line.Trim())
    Alerte 'Envoie le fichier WorldRadio-installation.log pour qu on regarde.'
    Terminer 1
}
