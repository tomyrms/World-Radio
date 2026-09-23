# ============================================================================
#  Ou est installe GTA V Enhanced, sur CETTE machine
# ----------------------------------------------------------------------------
#  Aucun nom de dossier n'est suppose. Epic installe souvent dans "GTAV",
#  Steam dans "Grand Theft Auto V Enhanced", et chacun peut choisir un autre
#  disque ou un autre nom. Le seul critere : le dossier contient
#  GTA5_Enhanced.exe.
#
#  Sources, de la plus sure a la plus lente :
#    1. le jeu en cours d'execution, s'il est lance
#    2. les manifestes du launcher Epic Games
#    3. les bibliotheques Steam (libraryfolders.vdf), toutes, sur tous disques
#    4. le launcher Rockstar (base de registre)
#    5. la liste des programmes installes de Windows
#    6. en dernier recours, une recherche sur les disques
#
#  Chaque source est facultative, pour qu'un test puisse fabriquer une fausse
#  installation Epic ou Steam sans dependre de la machine qui le lance.
# ============================================================================

function Trouve-Candidats {
    param(
        [string]$ProgramData = $env:ProgramData,
        [string[]]$Steam = $null,          # null : lu dans le registre
        [string[]]$Disques = $null,        # null : tous les disques fixes
        [switch]$SansRegistre,
        [switch]$SansProcessus,
        [switch]$SansScan,
        [scriptblock]$Trace = { param($m) }
    )

    $res = New-Object System.Collections.ArrayList

    $ajouter = {
        param($dossier, $source)
        if (-not $dossier) { return }
        $d = $null
        try { $d = [IO.Path]::GetFullPath(([string]$dossier).Replace('/', '\').Trim().Trim('"').TrimEnd('\')) } catch { return }
        if (-not $d) { return }
        if (-not (Test-Path -LiteralPath (Join-Path $d 'GTA5_Enhanced.exe'))) { return }
        foreach ($c in $res) { if ($c.Dossier -ieq $d) { return } }
        [void]$res.Add([PSCustomObject]@{ Dossier = $d; Source = $source })
        & $Trace ("trouve (" + $source + ") : " + $d)
    }

    # --- 1. le jeu lance ------------------------------------------------------
    if (-not $SansProcessus) {
        foreach ($p in (Get-Process -Name GTA5_Enhanced, PlayGTAV -ErrorAction SilentlyContinue)) {
            try { & $ajouter (Split-Path -Parent $p.Path) 'jeu lance' } catch { }
        }
    }

    # --- 2. Epic Games --------------------------------------------------------
    #  Chaque jeu installe a son manifeste .item, en JSON, avec InstallLocation.
    if ($ProgramData) {
        $man = Join-Path $ProgramData 'Epic\EpicGamesLauncher\Data\Manifests'
        if (Test-Path -LiteralPath $man) {
            & $Trace ("Epic : " + $man)
            foreach ($f in (Get-ChildItem -LiteralPath $man -Filter '*.item' -ErrorAction SilentlyContinue)) {
                try {
                    $j = Get-Content -LiteralPath $f.FullName -Raw | ConvertFrom-Json
                    & $ajouter $j.InstallLocation 'Epic Games'
                } catch { & $Trace ("  manifeste illisible : " + $f.Name) }
            }
        }
        $dat = Join-Path $ProgramData 'Epic\UnrealEngineLauncher\LauncherInstalled.dat'
        if (Test-Path -LiteralPath $dat) {
            try {
                $l = Get-Content -LiteralPath $dat -Raw | ConvertFrom-Json
                foreach ($e in $l.InstallationList) { & $ajouter $e.InstallLocation 'Epic Games' }
            } catch { & $Trace '  LauncherInstalled.dat illisible' }
        }
    }

    # --- 3. Steam ------------------------------------------------------------
    #  libraryfolders.vdf liste TOUTES les bibliotheques, sur tous les disques.
    $racinesSteam = @()
    if ($Steam) { $racinesSteam = $Steam }
    elseif (-not $SansRegistre) {
        foreach ($k in @(@('HKCU:\Software\Valve\Steam', 'SteamPath'),
                         @('HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'InstallPath'),
                         @('HKLM:\SOFTWARE\Valve\Steam', 'InstallPath'))) {
            try {
                $v = (Get-ItemProperty -Path $k[0] -ErrorAction Stop).($k[1])
                if ($v) { $racinesSteam += ([string]$v).Replace('/', '\') }
            } catch { }
        }
    }
    $bibliotheques = @()
    foreach ($s in $racinesSteam) {
        $bibliotheques += $s
        $vdf = Join-Path $s 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) {
            $txt = Get-Content -LiteralPath $vdf -Raw
            foreach ($m in [regex]::Matches($txt, '"path"\s+"([^"]+)"')) {
                # dans ce fichier, les antislashs sont doubles : D:\\SteamLibrary
                $bibliotheques += $m.Groups[1].Value.Replace('\\', '\')
            }
        }
    }
    foreach ($b in ($bibliotheques | Select-Object -Unique)) {
        & $Trace ("Steam : bibliotheque " + $b)
        # 3240220 : l'identifiant Steam de GTA V Enhanced
        $acf = Join-Path $b 'steamapps\appmanifest_3240220.acf'
        if (Test-Path -LiteralPath $acf) {
            $m = [regex]::Match((Get-Content -LiteralPath $acf -Raw), '"installdir"\s+"([^"]+)"')
            if ($m.Success) { & $ajouter (Join-Path $b ('steamapps\common\' + $m.Groups[1].Value)) 'Steam' }
        }
        $common = Join-Path $b 'steamapps\common'
        if (Test-Path -LiteralPath $common) {
            foreach ($d in (Get-ChildItem -LiteralPath $common -Directory -ErrorAction SilentlyContinue)) {
                & $ajouter $d.FullName 'Steam'
            }
        }
    }

    # --- 4. launcher Rockstar ------------------------------------------------
    if (-not $SansRegistre) {
        foreach ($k in 'HKLM:\SOFTWARE\WOW6432Node\Rockstar Games\GTAV Enhanced',
                       'HKLM:\SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V Enhanced',
                       'HKLM:\SOFTWARE\Rockstar Games\GTAV Enhanced',
                       'HKLM:\SOFTWARE\Rockstar Games\Grand Theft Auto V Enhanced',
                       'HKLM:\SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V',
                       'HKLM:\SOFTWARE\Rockstar Games\Grand Theft Auto V') {
            try { & $ajouter (Get-ItemProperty -Path $k -ErrorAction Stop).InstallFolder 'Rockstar' } catch { }
        }

        # --- 5. programmes installes de Windows -------------------------------
        foreach ($k in 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
                       'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
                       'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*') {
            foreach ($e in (Get-ItemProperty -Path $k -ErrorAction SilentlyContinue)) {
                if ($e.DisplayName -match 'Grand Theft Auto|GTA ?V') { & $ajouter $e.InstallLocation 'Windows' }
            }
        }
    }

    # --- 6. recherche sur les disques, si tout le reste a echoue --------------
    if ($res.Count -eq 0 -and -not $SansScan) {
        $racines = $Disques
        if (-not $racines) {
            $racines = @(Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue |
                         Where-Object { $_.Root -match '^[A-Z]:\\$' -and $_.Free -ne $null } |
                         ForEach-Object { $_.Root })
        }
        $ignores = 'Windows', '$Recycle.Bin', 'System Volume Information', 'ProgramData', 'Recovery', 'PerfLogs'
        foreach ($r in $racines) {
            & $Trace ("recherche sur " + $r)
            foreach ($sd in (Get-ChildItem -LiteralPath $r -Directory -ErrorAction SilentlyContinue)) {
                if ($ignores -contains $sd.Name) { continue }
                & $ajouter $sd.FullName 'recherche'
                # quatre niveaux suffisent : X:\Jeux\Epic Games\GTAV\GTA5_Enhanced.exe
                foreach ($exe in (Get-ChildItem -LiteralPath $sd.FullName -Filter 'GTA5_Enhanced.exe' -File -Recurse -Depth 4 -ErrorAction SilentlyContinue)) {
                    & $ajouter $exe.DirectoryName 'recherche'
                }
            }
        }
    }

    # Sortie en pipeline, un element par candidat. L'appelant DOIT l'envelopper
    # dans @( ) : sous PowerShell 5.1, un resultat unique serait sinon deroule
    # en objet simple, qui n'a pas de .Count dans cette version. L'installeur
    # aurait alors cru n'avoir rien trouve alors qu'il tenait le jeu.
    $res
}

# ----------------------------------------------------------------------------
#  Dernier recours : le joueur montre lui-meme l'executable
# ----------------------------------------------------------------------------
function Demander-Exe {
    try {
        Add-Type -AssemblyName System.Windows.Forms
        $proprietaire = New-Object System.Windows.Forms.Form
        $proprietaire.TopMost = $true            # sinon la fenetre s'ouvre derriere la console
        $dlg = New-Object System.Windows.Forms.OpenFileDialog
        $dlg.Title = 'Ou est installe GTA V Enhanced ? Selectionne GTA5_Enhanced.exe'
        $dlg.Filter = 'GTA V Enhanced (GTA5_Enhanced.exe)|GTA5_Enhanced.exe|Tous les executables (*.exe)|*.exe'
        $dlg.CheckFileExists = $true
        $choix = $dlg.ShowDialog($proprietaire)
        $proprietaire.Dispose()
        if ($choix -eq [System.Windows.Forms.DialogResult]::OK) { return $dlg.FileName }
    } catch { }
    return $null
}
