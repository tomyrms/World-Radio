# ============================================================================
#  A pied, pas de radio
# ----------------------------------------------------------------------------
#  Dans GTA d'origine, la radio n'existe qu'en vehicule. L'ancienne version
#  appelait SET_MOBILE_RADIO_ENABLED_DURING_GAMEPLAY(true) en sortant du
#  vehicule, ce qui ouvrait au joueur la radio native a pied : l'inverse du
#  comportement voulu. Ce test garantit que ca ne peut pas revenir.
# ============================================================================

$ErrorActionPreference = 'Stop'
$W = Split-Path -Parent $PSScriptRoot

$echecs = 0
function Ok($m)    { Write-Host ('  ok    : ' + $m) -ForegroundColor Green }
function Echec($m) { Write-Host ('  ECHEC : ' + $m) -ForegroundColor Red; $script:echecs++ }

Write-Host ''
Write-Host '=== radio a pied ===' -ForegroundColor Cyan

$sources = Get-ChildItem (Join-Path $W 'src') -Filter '*.cs' | ForEach-Object { Get-Content $_.FullName -Raw }
$tout = $sources -join "`n"
$natif = Get-Content (Join-Path $W 'src\Natif.cs') -Raw
$jeu = Get-Content (Join-Path $W 'src\WorldRadio.cs') -Raw

# --- 1. la native n'est jamais appelee avec « vrai » ------------------------
$appels = [regex]::Matches($tout, 'SET_MOBILE_RADIO_ENABLED_DURING_GAMEPLAY\s*,\s*(\w+)')
$vrais = @($appels | Where-Object { $_.Groups[1].Value -ne 'false' })
if ($appels.Count -ge 1 -and $vrais.Count -eq 0) {
    Ok ("la radio a pied n est jamais activee ({0} appel(s), tous a false)" -f $appels.Count)
} else {
    Echec ("la radio a pied peut etre activee : " + (($vrais | ForEach-Object { $_.Value }) -join ' | '))
}

# --- 2. l'ancienne fonction a parametre a disparu ---------------------------
if ($tout -notmatch 'RadioMobile\s*\(') {
    Ok 'RadioMobile(bool) n existe plus : impossible de repasser vrai par erreur'
} else {
    Echec 'RadioMobile est encore la, et accepte encore vrai'
}

# --- 3. bloquee des le demarrage --------------------------------------------
$dCtor = $jeu.IndexOf('public WorldRadioScript()')
$fCtor = $jeu.IndexOf('KeyDown +=', $dCtor)
if ($dCtor -ge 0 -and $fCtor -gt $dCtor -and $jeu.Substring($dCtor, $fCtor - $dCtor) -match 'BloquerRadioAPied\(\)') {
    Ok 'la radio a pied est fermee des le demarrage du script'
} else {
    Echec 'rien ne ferme la radio a pied au demarrage'
}

# --- 4. la garde a pied tourne a chaque image --------------------------------
if ($jeu -match 'GarderPieton\(\);' -and $jeu -match 'private void GarderPieton\(\)') {
    Ok 'la garde a pied est appelee a chaque image'
} else {
    Echec 'la garde a pied n est pas appelee'
}
$dG = $jeu.IndexOf('private void GarderPieton()')
$fG = $jeu.IndexOf("`n        }", $dG)
$blocG = $jeu.Substring($dG, $fG - $dG)
if ($blocG -match 'DesactiverCommande\(GTA\.Control\.VehicleRadioWheel\)') {
    Ok 'a pied, la commande de la roue est neutralisee'
} else {
    Echec 'a pied, la commande de la roue reste active'
}
if ($blocG -match 'BloquerRadioAPied') {
    Ok 'a pied, la native est reaffirmee regulierement'
} else {
    Echec 'la native n est pas reaffirmee : un autre script pourrait la rouvrir'
}
if ($blocG -match 'if \(EnVehicule\(\)\)') {
    Ok 'en vehicule, la garde ne touche a rien'
} else {
    Echec 'la garde agit aussi en vehicule'
}

# --- 5. notre propre roue reste reservee au vehicule -------------------------
$menu = Get-Content (Join-Path $W 'src\Menu.cs') -Raw
if ($menu -match 'if \(!Natif\.DansVehicule\(\)\) \{ Fermer\(\); return ActionMenu\.Aucune; \}') {
    Ok 'la roue de World Radio ne s ouvre qu en vehicule'
} else {
    Echec 'la roue de World Radio peut s ouvrir a pied'
}

Write-Host ''
if ($echecs -eq 0) { Write-Host '=== PIETON : TOUS LES TESTS PASSENT ===' -ForegroundColor Green }
else { Write-Host ("=== PIETON : {0} ECHEC(S) ===" -f $echecs) -ForegroundColor Red; exit 1 }
