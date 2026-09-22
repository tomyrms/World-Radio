// ============================================================================
//  Natif  -  acces aux fonctions du jeu que SHVDN n'expose pas directement
// ----------------------------------------------------------------------------
//  Trois besoins que l'API C# ne couvre pas, ou couvre de facon ambigue :
//
//  1. Lire une commande QUE L'ON A SOI-MEME DESACTIVEE. C'est ce qui permet
//     d'empecher la roue de GTA de s'ouvrir tout en continuant a lire le
//     bouton pour ouvrir la notre. IS_CONTROL_PRESSED renverrait faux sur une
//     commande desactivee, il faut IS_DISABLED_CONTROL_PRESSED.
//
//  2. Detecter le MENU PAUSE. Game.IsPaused ne correspond pas au menu pause
//     ouvert par le joueur, d'ou la radio qui continuait pendant la pause.
//
//  3. Lire les reglages audio du joueur, pour que la radio suive le curseur
//     "volume de la musique" du jeu au lieu de vivre sa vie.
//        301 = volume de la musique, identifie par balayage sur Enhanced
//        318 = couper le son quand le jeu perd le focus
// ============================================================================

using System;
using GTA.Native;

namespace WorldRadio
{
    internal static class Natif
    {
        // 301, et non 306. Le balayage des reglages l a designe sans ambiguite
        // quand le joueur a bouge le curseur Musique :
        //     [Reglages] reglage 301 : 1 -> 3
        // au moment meme ou GET_MUSIC_VOL_SLIDER passait de 1 a 3. Le 306,
        // qui renvoyait 9 en permanence, designe autre chose sur Enhanced.
        internal const int ReglageVolumeMusique = 301;
        internal const int ReglageCouperHorsFocus = 318;

        // ---- commandes ----

        /// <summary>Empeche le jeu d'agir sur cette commande pour l'image en cours.</summary>
        internal static void DesactiverCommande(GTA.Control c)
        {
            try { Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)c, true); }
            catch { }
        }

        /// <summary>Etat d'une commande, meme si elle a ete desactivee.</summary>
        internal static bool CommandeEnfoncee(GTA.Control c)
        {
            try { return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)c); }
            catch { return false; }
        }

        /// <summary>Valeur d'un axe entre -1 et 1, meme si l'axe a ete desactive.</summary>
        internal static float ValeurAxe(GTA.Control c)
        {
            try { return Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, (int)c); }
            catch { return 0f; }
        }

        /// <summary>Front d'appui, meme si la commande a ete desactivee.</summary>
        internal static bool CommandeVientDEtrePressee(GTA.Control c)
        {
            try { return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)c); }
            catch { return false; }
        }

        /// <summary>
        /// Ralentit le temps, comme le fait la roue des radios d'origine :
        /// on choisit sa station sans continuer a foncer. 1 = vitesse normale.
        /// Toujours remise a 1 en fermant, et a l'arret du script.
        /// </summary>
        internal static void EchelleDuTemps(float echelle)
        {
            if (echelle < 0.05f) echelle = 0.05f;
            if (echelle > 1f) echelle = 1f;
            try { Function.Call(Hash.SET_TIME_SCALE, echelle); }
            catch (Exception ex) { Journal.Erreur("echelle du temps", ex); }
        }

        // Boutons candidats pour changer de pays. Les manettes ne mappent pas
        // toutes les memes commandes sur les gachettes d'epaule : plutot que
        // de parier, on note ce qui est REELLEMENT presse pendant la roue.
        private static readonly GTA.Control[] Candidats =
        {
            GTA.Control.VehicleHorn,              // 86
            GTA.Control.VehicleHandbrake,         // 76
            GTA.Control.VehicleDuck,              // 73
            GTA.Control.VehicleHeadlight,         // 74
            GTA.Control.VehicleLookBehind,        // 79
            GTA.Control.VehicleCinCam,            // 80
            GTA.Control.VehicleNextRadio,         // 81
            GTA.Control.VehiclePrevRadio,         // 82
            GTA.Control.VehicleNextRadioTrack,    // 83
            GTA.Control.VehiclePrevRadioTrack,    // 84
            GTA.Control.VehicleSelectNextWeapon,  // 99
            GTA.Control.VehicleSelectPrevWeapon   // 100
        };

        /// <summary>
        /// Journalise les commandes pressees pendant que la roue est ouverte.
        /// Sert a identifier ce que la manette envoie vraiment, au lieu de le
        /// deviner. N'a lieu que si AudioLog est actif.
        /// </summary>
        internal static void TracerBoutonsRoue()
        {
            for (int i = 0; i < Candidats.Length; i++)
                if (CommandeVientDEtrePressee(Candidats[i]))
                    Journal.Ecrire("[roue] bouton presse : " + Candidats[i]
                                   + " (" + (int)Candidats[i] + ")");
        }

        // ---- etat du jeu ----

        /// <summary>
        /// Le menu pause du joueur est-il ouvert.
        /// Deux sources : la native dediee, et l'etat du menu. GET_PAUSE_MENU_STATE
        /// renvoie 0 quand aucun menu n'est ouvert ; toute autre valeur signifie
        /// qu'un ecran de pause est affiche ou en transition.
        /// </summary>
        internal static bool MenuPauseOuvert()
        {
            try
            {
                if (Function.Call<bool>(Hash.IS_PAUSE_MENU_ACTIVE)) return true;
                return Function.Call<int>(Hash.GET_PAUSE_MENU_STATE) != 0;
            }
            catch { return false; }
        }

        /// <summary>Les deux sources separement, pour le diagnostic.</summary>
        internal static bool PauseNative()
        {
            try { return Function.Call<bool>(Hash.IS_PAUSE_MENU_ACTIVE); }
            catch { return false; }
        }

        internal static int EtatMenuPause()
        {
            try { return Function.Call<int>(Hash.GET_PAUSE_MENU_STATE); }
            catch { return -1; }
        }

        /// <summary>Horloge du jeu. Elle s'arrete quand le jeu est fige.</summary>
        internal static int HorlogeJeu()
        {
            try { return GTA.Game.GameTime; }
            catch { return 0; }
        }

        /// <summary>Le joueur est-il a bord d un vehicule.</summary>
        internal static bool DansVehicule()
        {
            try
            {
                GTA.Ped p = GTA.Game.Player.Character;
                return p != null && p.Exists() && p.IsInVehicle();
            }
            catch { return false; }
        }

        /// <summary>
        /// Une cinematique JOUE en ce moment.
        ///
        /// Surtout pas Game.IsCutsceneActive : il appelle IS_CUTSCENE_ACTIVE,
        /// qui devient vrai des qu'une cinematique est CHARGEE en memoire. GTA
        /// la precharge en approchant d'un declencheur de mission, donc la
        /// radio se coupait en passant simplement a cote, sans que rien ne se
        /// joue. Verifie sur l'IL de SHVDN : la propriete pousse bien le hash
        /// 0x991251AFC3981F84, soit IS_CUTSCENE_ACTIVE.
        ///
        /// IS_CUTSCENE_PLAYING, lui, n'est vrai que pendant la lecture.
        /// </summary>
        internal static bool SceneCinematique()
        {
            try { return Function.Call<bool>(Hash.IS_CUTSCENE_PLAYING); }
            catch { return false; }
        }

        /// <summary>
        /// Une mission scriptee est en cours. C'est le signal qu'il fallait :
        /// il ne devient vrai qu'une fois la mission REELLEMENT commencee, pas
        /// quand on passe a cote de son declencheur.
        /// </summary>
        internal static bool EnMission()
        {
            try { return Function.Call<bool>(Hash.GET_MISSION_FLAG); }
            catch { return false; }
        }

        /// <summary>
        /// Quelqu'un parle : dialogue scripte, replique d'ambiance du joueur,
        /// ou appel telephonique. C'est pour ces moments-la que la radio doit
        /// s'effacer, pas pour la proximite d'un point sur la carte.
        /// </summary>
        internal static bool DialogueEnCours()
        {
            try
            {
                if (Function.Call<bool>(Hash.IS_MOBILE_PHONE_CALL_ONGOING)) return true;

                int joueur = 0;
                try { joueur = GTA.Game.Player.Character.Handle; } catch { }
                if (joueur != 0 && Function.Call<bool>(Hash.IS_SCRIPTED_SPEECH_PLAYING, joueur)) return true;

                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// Le joueur est mort ou se fait arreter. L'ecran « Wasted » avec la
        /// radio qui continue, c'est rate.
        /// </summary>
        internal static bool JoueurHorsJeu()
        {
            try
            {
                int j = GTA.Game.Player.Handle;
                if (Function.Call<bool>(Hash.IS_PLAYER_DEAD, j)) return true;
                if (Function.Call<bool>(Hash.IS_PLAYER_BEING_ARRESTED, j, false)) return true;
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// Transition entre Michael, Franklin et Trevor. Elle a sa propre
        /// ambiance sonore, la radio n'a rien a y faire.
        /// </summary>
        internal static bool ChangementPersonnage()
        {
            try { return Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS); }
            catch { return false; }
        }

        /// <summary>
        /// Une replique d'ambiance joue. Deconseille comme cause d'attenuation :
        /// un passant qui rale suffirait a faire plonger la musique. Fourni
        /// pour qui veut essayer, desactive par defaut.
        /// </summary>
        internal static bool RepliqueAmbiante()
        {
            try
            {
                int p = GTA.Game.Player.Character.Handle;
                return Function.Call<bool>(Hash.IS_AMBIENT_SPEECH_PLAYING, p);
            }
            catch { return false; }
        }

        /// <summary>
        /// Le vehicule courant ne peut pas diffuser : moteur coupe, velo,
        /// epave, ou sous l'eau. Renvoie false des qu'il n'y a pas de vehicule
        /// du tout : ce cas-la est traite ailleurs, et le confondre ferait
        /// couper deux fois pour la meme raison.
        /// </summary>
        internal static bool VehiculeInapte()
        {
            try
            {
                GTA.Ped joueur = GTA.Game.Player.Character;
                if (joueur == null) return false;
                GTA.Vehicle v = joueur.CurrentVehicle;
                if (v == null || !v.Exists()) return false;

                // Le velo n'a pas d'autoradio, dans GTA comme ailleurs.
                if (v.ClassType == GTA.VehicleClass.Cycles) return true;

                // Epave : plus de tableau de bord, plus de radio.
                if (v.IsDead || v.EngineHealth <= 0f) return true;

                // A demi immerge, l'electronique rend l'ame.
                if (v.SubmersionLevel > 0.72f) return true;

                // Moteur coupe : c'est ce que fait le jeu lui-meme.
                if (!v.IsEngineRunning && !v.IsEngineStarting) return true;

                return false;
            }
            catch { return false; }
        }

        internal static bool JoueurAuxCommandes()
        {
            try { return Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, GTA.Game.Player); }
            catch { return true; }
        }

        internal static bool EcranNoir()
        {
            try { return Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT); }
            catch { return false; }
        }

        /// <summary>
        /// Le joueur agit-il reellement dans le jeu. Pendant que le menu pause
        /// a le focus, le jeu neutralise ces commandes : en voir une active est
        /// donc la preuve qu'on est revenu en jeu.
        ///
        /// On lit les commandes NON desactivees, a l'inverse du selecteur radio
        /// qui lit les siennes malgre leur neutralisation.
        /// </summary>
        internal static bool JoueurAgit()
        {
            try
            {
                foreach (int c in new[] { 71, 72, 32, 33, 34, 35, 24, 22 })
                {
                    if (Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, c)) return true;
                }
                // regard a la souris ou au stick droit
                if (Math.Abs(Function.Call<float>(Hash.GET_CONTROL_NORMAL, 0, 1)) > 0.25f) return true;
                if (Math.Abs(Function.Call<float>(Hash.GET_CONTROL_NORMAL, 0, 2)) > 0.25f) return true;
            }
            catch { }
            return false;
        }

        // ------------------------------------------------------------------
        //  Recherche du bon reglage de volume
        // ------------------------------------------------------------------
        //  GET_MUSIC_VOL_SLIDER renvoie 6 et ne bouge pas, le reglage 306 en
        //  renvoie 9 : aucun des deux ne suit le curseur du jeu sur Enhanced.
        //  Plutot que d'essayer un identifiant apres l'autre, on surveille
        //  toute une plage et on signale celui qui CHANGE quand le joueur
        //  bouge le curseur. Le jeu designe ainsi lui-meme le bon.
        private const int PremierReglage = 295;
        private const int DernierReglage = 325;
        private static int[] _reglagesVus;

        /// <summary>
        /// Renvoie la description des reglages qui ont change depuis le dernier
        /// appel, ou null si rien n'a bouge.
        /// </summary>
        internal static string ReglagesQuiChangent()
        {
            try
            {
                int n = DernierReglage - PremierReglage + 1;
                bool premier = _reglagesVus == null;
                if (premier) _reglagesVus = new int[n];

                string change = null;
                for (int i = 0; i < n; i++)
                {
                    int v = ReglageBrut(PremierReglage + i);
                    if (!premier && v != _reglagesVus[i])
                    {
                        string ligne = "reglage " + (PremierReglage + i)
                                     + " : " + _reglagesVus[i] + " -> " + v;
                        change = change == null ? ligne : change + "   |   " + ligne;
                    }
                    _reglagesVus[i] = v;
                }
                return change;
            }
            catch { return null; }
        }

        /// <summary>Consigne d'un coup tous les signaux candidats de pause.</summary>
        internal static string SondePause()
        {
            return "IS_PAUSE_MENU_ACTIVE=" + PauseNative()
                 + "  PAUSE_MENU_STATE=" + EtatMenuPause()
                 + "  cinematique=" + SceneCinematique()
                 + "  ecranNoir=" + EcranNoir()
                 + "  auxCommandes=" + JoueurAuxCommandes()
                 + "  joueurAgit=" + JoueurAgit()
                 + "  horlogeJeu=" + HorlogeJeu();
        }

        /// <summary>
        /// Consigne l'environnement reellement charge. Melanger l'ASI d'une
        /// version avec les DLL d'une autre produit des pannes silencieuses :
        /// autant que le journal le prouve des la premiere ligne.
        /// </summary>
        internal static void JournaliserEnvironnement(string versionMod)
        {
            try
            {
                string shvdn = "inconnu";
                try { shvdn = typeof(GTA.Script).Assembly.GetName().Version.ToString(); }
                catch { }

                Journal.Ecrire("[Environment] GTA = Enhanced");
                Journal.Ecrire("[Environment] SHVDNE = " + shvdn);
                Journal.Ecrire("[Environment] WorldRadio = " + versionMod);

                string dossier = Dossiers.Jeu();
                if (dossier == null) return;
                foreach (string f in new[] { "ScriptHookVDotNet.asi",
                                             "ScriptHookVDotNet2.dll",
                                             "ScriptHookVDotNet3.dll" })
                {
                    string p = System.IO.Path.Combine(dossier, f);
                    if (!System.IO.File.Exists(p)) { Journal.Ecrire("[Environment] " + f + " ABSENT"); continue; }
                    var fi = System.Diagnostics.FileVersionInfo.GetVersionInfo(p);
                    Journal.Ecrire("[Environment] " + f + " = " + fi.FileVersion
                                   + "   " + System.IO.File.GetLastWriteTime(p).ToString("yyyy-MM-dd"));
                }
            }
            catch (Exception ex) { Journal.Erreur("journal d'environnement", ex); }
        }

        /// <summary>
        /// Empeche le jeu de mettre CE script en pause.
        ///
        /// Sans cela, GTA suspend le script des l'ouverture du menu pause : le
        /// tick ne tourne plus, et le mod n'a donc aucune occasion de baisser
        /// son propre volume. C'est la cause du son qui continuait pendant la
        /// pause, et aucune detection de pause ne pouvait la corriger.
        /// </summary>
        internal static void NePasMettreEnPause()
        {
            try
            {
                Function.Call(Hash.SET_THIS_SCRIPT_CAN_BE_PAUSED, false);
                Journal.Ecrire("script exempte de la pause du jeu");
            }
            catch (Exception ex) { Journal.Erreur("SET_THIS_SCRIPT_CAN_BE_PAUSED", ex); }
        }

        /// <summary>
        /// Curseur "volume de la musique" des reglages audio, ramene entre 0 et 1.
        /// Source principale, plus directe que le reglage de profil.
        /// Renvoie -1 si la valeur est illisible ou hors des plages connues.
        /// </summary>
        internal static float VolumeMusique()
        {
            try
            {
                int v = Function.Call<int>(Hash.GET_MUSIC_VOL_SLIDER);
                return Normaliser(v);
            }
            catch { return -1f; }
        }

        /// <summary>
        /// On ne suppose pas la plage : le curseur est documente de 0 a 10,
        /// mais certaines versions exposent 0 a 100.
        /// </summary>
        internal static float Normaliser(int v)
        {
            if (v < 0) return -1f;
            if (v <= 10) return v / 10f;
            if (v <= 100) return v / 100f;
            return -1f;
        }

        /// <summary>Valeur brute du curseur, pour le journal de diagnostic.</summary>
        internal static int VolumeMusiqueBrut()
        {
            try { return Function.Call<int>(Hash.GET_MUSIC_VOL_SLIDER); }
            catch { return -1; }
        }

        internal static int ReglageBrut(int identifiant)
        {
            try { return Function.Call<int>(Hash.GET_PROFILE_SETTING, identifiant); }
            catch { return -1; }
        }

        /// <summary>
        /// Le joueur vient-il d'appuyer sur la touche qui ouvre le menu pause.
        ///
        /// IS_PAUSE_MENU_ACTIVE ne devient vrai qu'une fois le menu affiche :
        /// sans cette detection anticipee, la radio reste audible par-dessus
        /// le menu pendant une fraction de seconde.
        ///      199 = pause (Echap, Start)
        ///      200 = pause, variante
        /// </summary>
        internal static bool PauseVientDEtreDemandee()
        {
            try
            {
                // UNIQUEMENT le front, jamais l'appui maintenu.
                //
                // Tester l'appui maintenu semblait plus sur, mais le journal a
                // montre l'inverse : ces commandes lisent vrai en jeu normal,
                // et le verrou s'armait puis se levait 33 fois dans la meme
                // seconde. Le son en ressortait hache.
                //
                // Le front seul rate parfois l'image de l'appui, mais Echap est
                // rattrape par le hook clavier, qui lui ne rate rien.
                foreach (int c in new[] { 199, 200 })
                {
                    if (Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, c)) return true;
                }
            }
            catch { }
            return false;
        }

        // ---- reglages du joueur ----

        /// <summary>
        /// Reglage audio du joueur, ramene entre 0 et 1.
        /// Le jeu expose des curseurs de 0 a 10 ; on tolere une echelle sur
        /// 100 au cas ou, et on renvoie -1 si le reglage est illisible.
        /// </summary>
        internal static float Reglage01(int identifiant)
        {
            try
            {
                int v = Function.Call<int>(Hash.GET_PROFILE_SETTING, identifiant);
                if (v < 0) return -1f;
                if (v <= 10) return v / 10f;
                if (v <= 100) return v / 100f;
                return -1f;
            }
            catch { return -1f; }
        }

        internal static bool ReglageActif(int identifiant)
        {
            try { return Function.Call<int>(Hash.GET_PROFILE_SETTING, identifiant) != 0; }
            catch { return false; }
        }

        // ---- radio du jeu ----

        /// <summary>
        /// Eteint la radio du vehicule. A reappliquer regulierement : le jeu la
        /// rallume de lui-meme a l'entree dans un vehicule, et il n'existe
        /// aucun accesseur en lecture pour savoir dans quel etat elle est.
        /// </summary>
        internal static void EteindreRadioVehicule(int handleVehicule)
        {
            try
            {
                Function.Call(Hash.SET_VEHICLE_RADIO_ENABLED, handleVehicule, false);
                Function.Call(Hash.SET_VEH_RADIO_STATION, handleVehicule, "OFF");
            }
            catch { }
        }

        internal static void AllumerRadioVehicule(int handleVehicule)
        {
            try { Function.Call(Hash.SET_VEHICLE_RADIO_ENABLED, handleVehicule, true); }
            catch { }
        }

        /// <summary>Radio du telephone, qui joue aussi a pied.</summary>
        internal static void RadioMobile(bool active)
        {
            try { Function.Call(Hash.SET_MOBILE_RADIO_ENABLED_DURING_GAMEPLAY, active); }
            catch { }
        }
    }
}
