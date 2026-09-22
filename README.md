# World Radio

Radios internet réelles pour **GTA V Enhanced**. Dix-sept stations, cinq pays,
avec l'artiste et le titre lus directement dans le flux.

Le son ne vient pas du moteur audio de GTA — ScriptHookVDotNet n'y donne aucun
accès. Le mod prend donc à sa charge tout ce que Rockstar ferait : volume
asservi au curseur « Musique », silence en pause, en arrière-plan et à pied,
extinction de la radio du véhicule pendant l'écoute.

## Les stations

| Pays | Stations |
|---|---|
| France | Skyrock, NRJ, RFM |
| Suisse | One FM, Rouge FM, LFM |
| Portugal | Rádio Comercial, RFM, M80, Cidade FM |
| Espagne | LOS40, Cadena 100, Cadena Dial |
| États-Unis | Z100, KIIS FM, HOT 97, KROQ |

Quatorze stations annoncent le morceau en cours. Trois ne le font pas — NRJ,
RFM France et KROQ n'exposent aucune source accessible. L'encart affiche alors
« EN DIRECT » plutôt que d'inventer un titre.

## En jeu, au volant

| Geste | Effet |
|---|---|
| Touche radio, appui **bref** | éteint, ou rallume la dernière station |
| Touche radio, **maintenue** | ouvre la roue |
| Stick droit | **vise** une station — on pointe, on ne défile pas |
| Tout en haut | RADIO OFF, à la même place dans chaque pays |
| R1 / RB | pays suivant, en boucle |
| Relâcher | ferme |
| F10 / F9 | station suivante / précédente |
| NumPad9 / NumPad3 | volume par 10 % |

La station visée se lance aussitôt : on entend ce qu'on pointe. Le jeu passe au
ralenti pendant le choix, comme la roue d'origine.

## Quand la radio se tait

Chaque cause a son propre fondu, et toute remontée est plus lente que sa
descente — une radio qui revient d'un coup après un dialogue s'entend comme une
faute.

| Situation | Effet | Descente | Retour |
|---|---|---|---|
| Menu pause | coupe | net | 220 ms |
| Fenêtre en arrière-plan | coupe | 130 ms | 200 ms |
| Écran noir, chargement | coupe | 200 ms | 420 ms |
| Mort ou arrestation | coupe | 260 ms | 720 ms |
| Changement de personnage | coupe | 220 ms | 540 ms |
| Cinématique | coupe | 300 ms | 620 ms |
| Hors véhicule | coupe | 260 ms | 320 ms |
| Moteur coupé, vélo, épave, sous l'eau | coupe | 320 ms | 420 ms |
| **Mission ou dialogue** | **30 %** | 420 ms | 760 ms |

Pendant le menu pause, GTA gèle le fil des scripts : un mod gelé ne peut pas
baisser son propre volume. Un chien de garde tourne donc sur son propre fil et
coupe après 260 ms sans signe de vie du jeu.

## Compiler

```powershell
.\build\compile.ps1
```

Le script trouve GTA V Enhanced tout seul : registre, emplacements habituels sur
tous les disques, ou la variable `GTA5_ENHANCED` si tu la définis.

Compilé avec `csc.exe` du .NET Framework 4, en C# 5 — pas de `?.`, pas de `$""`.
C'est la contrainte de ScriptHookVDotNet.

## Installer

```powershell
.\install\install.ps1
```

Copie le DLL, la configuration, NAudio et les 24 images dans `scripts\`. Les
réglages personnels d'une installation précédente sont conservés : volume,
dernière station, touches, hauteur de l'encart.

## Tester

```powershell
.\tests\general.ps1     # configuration, flux, métadonnées, volume
.\tests\roue.ps1        # visée angulaire, proportions des logos, sélection
.\tests\fondu.ps1       # les dix causes de coupure, les rampes, le chien de garde
.\tests\spectre.ps1     # analyse de fréquence sur un flux réel
```

Ces tests ne vérifient pas que le code compile, mais qu'il fait ce qu'il
prétend : ils ouvrent de vrais flux, mesurent la durée réelle des fondus,
et lisent les dimensions réelles des images.

## Comment c'est fait

```
src/
  WorldRadio.cs     le script, le fil du jeu
  Audio.cs          SEULE autorité du volume
  Lecteur.cs        flux réseau, un ouvrier unique
  Spectre.cs        niveaux par transformée de Fourier
  Menu.cs           état de la roue, visée angulaire
  Selecteur.cs      rendu de la roue
  Apercu.cs         encart en bas à droite
  Metadonnees.cs    artiste et titre, hors du fil du jeu
  Natif.cs          appels aux natives de GTA
  Config.cs         lecture du .ini
  Dessin.cs         primitives et cache d'images
  Ecran.cs          géométrie, zone de sécurité
  Pause.cs          détection du gel du script
  Journal.cs        traces
  Systeme.cs        dossiers, focus fenêtre, proportions des images
```

Deux règles qui ont coûté cher, et qui tiennent le reste :

**Ne jamais écrire `IWavePlayer.Volume`.** Dans NAudio, cette propriété ne règle
pas le volume du flux : `WasapiOut.Volume` pointe sur le volume du périphérique
Windows entier. L'écrire baisse le son de tout le système. Le gain passe
exclusivement par un `VolumeSampleProvider` inséré dans la chaîne. Un test le
vérifie sur le DLL produit.

**Vérifier quelle native se cache derrière une propriété.** `Game.IsCutsceneActive`
appelle `IS_CUTSCENE_ACTIVE`, qui devient vrai dès qu'une cinématique est
*chargée* — GTA la précharge à l'approche d'un déclencheur, ce qui coupait la
radio en passant simplement à côté. C'est `IS_CUTSCENE_PLAYING` qu'il faut.

## Dépendances

- **ScriptHookV** — non redistribuable, à prendre sur [dev-c.com](http://www.dev-c.com/gtav/scripthookv/). Sa version doit correspondre à celle du jeu.
- **ScriptHookVDotNet** — licence zlib, [dépôt officiel](https://github.com/scripthookvdotnet/scripthookvdotnet)
- **NAudio** 1.10 — licence MIT, inclus dans `lib/`

## Licence

Aucune licence n'est déclarée pour l'instant : le dépôt est privé. Si tu le
passes en public, il faudra en choisir une, sans quoi le code reste « tous
droits réservés » par défaut.

Les logos et noms de stations appartiennent aux stations. Ils ne servent ici
qu'à les identifier.
