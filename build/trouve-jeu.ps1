# ============================================================================
#  Ou est installe GTA V Enhanced
# ----------------------------------------------------------------------------
#  Trois sources, dans cet ordre :
#      1. la variable d'environnement GTA5_ENHANCED, si tu l'as definie
#      2. la base de registre, quand le jeu a ete installe proprement
#      3. les emplacements habituels, sur tous les disques
#
#  A dot-sourcer :   . "$PSScriptRoot\trouve-jeu.ps1"
#                    $jeu = Trouve-Jeu
# ============================================================================

function Trouve-Jeu {
    param([string]$Impose = '')

    function Est-Enhanced($d) {
        return $d -and (Test-Path -LiteralPath (Join-Path $d 'GTA5_Enhanced.exe'))
    }

    if ($Impose) {
        if (Est-Enhanced $Impose) { return (Resolve-Path -LiteralPath $Impose).Path }
        throw "GTA5_Enhanced.exe introuvable dans : $Impose"
    }

    if ($env:GTA5_ENHANCED -and (Est-Enhanced $env:GTA5_ENHANCED)) {
        return (Resolve-Path -LiteralPath $env:GTA5_ENHANCED).Path
    }

    foreach ($cle in @(
        'HKLM:\SOFTWARE\WOW6432Node\Rockstar Games\GTAV Enhanced',
        'HKLM:\SOFTWARE\Rockstar Games\GTAV Enhanced',
        'HKLM:\SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V',
        'HKLM:\SOFTWARE\Rockstar Games\Grand Theft Auto V')) {
        try {
            $v = (Get-ItemProperty -Path $cle -ErrorAction Stop).InstallFolder
            if (Est-Enhanced $v) { return (Resolve-Path -LiteralPath $v).Path }
        } catch { }
    }

    foreach ($d in (Get-PSDrive -PSProvider FileSystem |
                    Where-Object { $_.Root -match '^[A-Z]:\\$' })) {
        foreach ($s in @('SteamLibrary\steamapps\common',
                         'Steam\steamapps\common',
                         'Program Files (x86)\Steam\steamapps\common',
                         'Program Files\Steam\steamapps\common',
                         'Program Files\Rockstar Games',
                         'Program Files (x86)\Rockstar Games',
                         'Program Files\Epic Games',
                         'Games', 'Jeux')) {
            $p = Join-Path $d.Root $s
            if (-not (Test-Path -LiteralPath $p)) { continue }
            try {
                foreach ($c in (Get-ChildItem $p -Directory -Filter 'Grand Theft Auto V*' -ErrorAction SilentlyContinue)) {
                    if (Est-Enhanced $c.FullName) { return $c.FullName }
                }
            } catch { }
        }
    }

    throw @'
GTA V Enhanced introuvable.

Definis son emplacement puis relance :
    $env:GTA5_ENHANCED = 'D:\Jeux\Grand Theft Auto V Enhanced'

C'est le dossier qui contient GTA5_Enhanced.exe.
'@
}
