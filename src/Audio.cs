// ============================================================================
//  ControleurAudio  -  autorite unique du volume
// ----------------------------------------------------------------------------
//  Le volume etait auparavant decide a trois endroits : au lancement d'une
//  station, aux touches, et dans la gestion de coupure. Trois auteurs pour une
//  meme valeur, donc des etats incoherents.
//
//  Desormais une seule chaine :
//
//      GTA (curseur Musique)  +  verrou de pause
//                               |
//                       ControleurAudio
//                               |
//                VolumeSampleProvider.Volume
//
//  Ni le menu, ni les stations, ni les metadonnees ne touchent au volume.
//  Le gain n'est jamais applique au peripherique Windows ni a la session du
//  processus : un test de compilation verifie cette regle sur la DLL.
//
//  Verrou de pause : arme des l'APPUI sur la touche, pas a l'ouverture du
//  menu. Si le tick devenait irregulier pendant l'ouverture du frontend, le
//  son serait deja coupe. Il se leve uniquement quand le jeu confirme que le
//  menu est referme, et le volume est alors RELU, jamais remis a 1.
// ============================================================================

using System;
using System.Threading;

namespace WorldRadio
{
    internal sealed class ControleurAudio
    {
        // Les fondus sont desormais exprimes en millisecondes, par raison
        // de coupure : voir Duree() et DureeRetour() plus bas.

        private readonly Lecteur _lecteur;
        private readonly DetecteurPause _pause = new DetecteurPause();

        private float _volumeJeu = 1f;       // curseur Musique de GTA, 0 a 1
        private float _proportion = 1f;      // reglage Volume du .ini
        private bool _verrouPause;
        private bool _suivreJeu = true;
        private bool _diag;

        private float _gainActuel;
        private float _gainCible;
        private int _compteurLecture;
        private int _derniereTrace;

        // on ne journalise que les vrais changements
        private int _dernierSlider = int.MinValue;
        private int _dernierProfil = int.MinValue;
        private bool _dernierePause;
        private int _derniereSonde;
        private string _derniereSondeTexte = "";
        private float _dernierGainTrace = -1f;

        internal ControleurAudio(Lecteur lecteur)
        {
            _lecteur = lecteur;
        }

        internal void Init(Config c)
        {
            _proportion = (float)Config.Borner(c.Volume);
            _attenuation = (float)Config.Borner(c.Attenuation);
            _repliquesAmbiantes = c.RepliquesAmbiantes;
            _suivreJeu = c.SuivreReglagesJeu;
            _diag = c.JournalAudio;
            _volumeJeu = 1f;
            _compteurLecture = 0;
            DemarrerGarde();
        }
        // ------------------------------------------------------------------
        //  Chien de garde
        // ------------------------------------------------------------------
        //  Pendant le menu pause, GTA gele le fil des scripts. Le mod ne tourne
        //  plus : il ne peut donc PAS baisser son propre volume, quelle que
        //  soit la finesse de la detection. C'est la raison de fond pour
        //  laquelle la musique continuait parfois par-dessus le menu, et
        //  aucune correction sur le fil du jeu ne pouvait y remedier.
        //
        //  Ce fil-ci, lui, ne gele pas. Il surveille la date du dernier tick :
        //  passe un certain silence, il coupe lui-meme. Le fil du jeu reprend
        //  la main des qu'il revit, et remonte le son en fondu.
        //
        //  Il ne fait qu'une chose, et seulement vers le bas : couper. Jamais
        //  remonter. Deux auteurs pour un meme volume, c'est ainsi qu'on
        //  fabrique des etats incoherents.
        private const int SilenceAvantCoupure = 260;   // ms sans tick

        private volatile int _dernierTick;
        private volatile bool _coupeParLaGarde;
        private Thread _garde;
        private volatile bool _gardeArretee;

        private void DemarrerGarde()
        {
            if (_garde != null) return;
            _dernierTick = Environment.TickCount;
            _garde = new Thread((ThreadStart)Veiller);
            _garde.IsBackground = true;
            _garde.Name = "WorldRadio-Garde";
            _garde.Start();
        }

        private void Veiller()
        {
            while (!_gardeArretee)
            {
                Thread.Sleep(40);
                if (_gardeArretee) return;

                int silence = Environment.TickCount - _dernierTick;
                if (silence < 0) { _dernierTick = Environment.TickCount; continue; }

                if (silence > SilenceAvantCoupure)
                {
                    if (!_coupeParLaGarde && _gainActuel > 0f)
                    {
                        _coupeParLaGarde = true;
                        try { _lecteur.DefinirGain(0f); } catch { }
                        if (_diag) Journal.Ecrire("[Garde] le jeu ne tourne plus depuis "
                                                  + silence + " ms : son coupe");
                    }
                }
            }
        }

        /// <summary>Met fin au chien de garde. Appele a l'arret du script.</summary>
        internal void Eteindre()
        {
            _gardeArretee = true;
        }


        internal void DefinirProportion(double v)
        {
            _proportion = (float)Config.Borner(v);
        }

        internal float GainActuel { get { return _gainActuel; } }


        // ------------------------------------------------------------------

        /// <summary>
        /// A appeler a chaque image. Relit l'etat du jeu, calcule le gain voulu
        /// et fait avancer la rampe. Aucun acces reseau, aucune allocation.
        ///
        /// CHAQUE CAUSE A SON FONDU. Le menu pause coupe net, parce qu'on ne
        /// doit rien entendre par-dessus lui. Un dialogue s'efface en douceur
        /// et revient plus doucement encore. Perdre le focus coupe vite, sans
        /// brutalite. Rien ne claque plus.
        /// </summary>
        internal void Rafraichir(bool horsVehicule)
        {
            // Le garde surveille cette date : tant qu'elle avance, le jeu vit.
            _dernierTick = Environment.TickCount;
            if (_coupeParLaGarde)
            {
                // Le jeu revient. On repart de zero et on remonte en fondu :
                // le gain reel a ete mis a zero par le garde, il faut que la
                // rampe le sache, sinon elle croirait n'avoir rien a faire.
                _coupeParLaGarde = false;
                _gainActuel = 0f;
                _verrouPause = true;          // on sort peut-etre du menu pause
            }

            LireMenuPause();
            LireVolumeJeu();

            // Le jeu n'a plus la main : on se tait, comme le ferait sa propre
            // radio. Sans cela le flux continuait de jouer alors que le joueur
            // avait bascule sur une autre fenetre.
            bool auJeu = Fenetre.AuPremierPlan();

            // Pendant la connexion il n'y a de toute facon rien a entendre :
            // viser zero permet a la station d'arriver en fondu.
            bool connexion = _lecteur.EnConnexion || _lecteur.Injoignable;

            // ATTENUATION, pas coupure. Pendant une mission, un dialogue ou une
            // cinematique, la radio passe derriere la voix au lieu de
            // disparaitre, puis remonte seule.
            //
            // On ne regarde PLUS IS_PLAYER_CONTROL_ON : le jeu retire la main
            // des qu'on approche d'un declencheur de mission, ce qui coupait la
            // radio en pleine rue sans qu'il se passe quoi que ce soit.
            //  cinematique  -> silence complet : c'est un moment narratif entier
            //  mission, dialogue, appel -> on passe derriere la voix
            bool cinematique = Natif.SceneCinematique();
            bool baisser = Natif.EnMission() || Natif.DialogueEnCours();
            if (_repliquesAmbiantes && Natif.RepliqueAmbiante()) baisser = true;

            // De la cause la plus imperative a la plus anodine. La premiere qui
            // repond l'emporte : deux coupures pour un meme instant n'auraient
            // pas de sens, et c'est la plus forte qui doit donner le ton.
            int raison;
            if (_verrouPause)                   { raison = RaisonPause; }
            else if (!auJeu)                    { raison = RaisonFocus; }
            else if (Natif.EcranNoir())         { raison = RaisonEcranNoir; }
            else if (Natif.JoueurHorsJeu())     { raison = RaisonHorsJeu; }
            else if (Natif.ChangementPersonnage()) { raison = RaisonBascule; }
            else if (cinematique)               { raison = RaisonCinematique; }
            else if (horsVehicule)              { raison = RaisonHorsVehicule; }
            else if (Natif.VehiculeInapte())    { raison = RaisonVehicule; }
            else if (connexion || _fondu)       { raison = RaisonStation; }
            else                                { raison = RaisonAucune; }

            bool couper = raison != RaisonAucune;

            // Le fondu de changement de station se termine des que la nouvelle
            // est connectee, SANS condition sur la raison courante : sinon une
            // autre coupure survenant pendant le changement laisserait le
            // drapeau arme, et le son ne reviendrait pas.
            if (_fondu && !connexion) _fondu = false;

            //  gain = coupe ? 0 : volume musique du jeu x proportion x attenuation
            //  Aucun autre multiplicateur ailleurs dans le mod.
            float plein = _volumeJeu * _proportion;
            float voulu = couper ? 0f : plein * (baisser ? _attenuation : 1f);
            if (_volumeJeu <= 0f) voulu = 0f;       // zero exact, aucun residu
            _gainCible = voulu;

            if (couper)
            {
                _dureeFondu = Duree(raison);
                _derniereRaison = raison;
            }
            else if (_derniereRaison != RaisonAucune)
            {
                // on sort d'une coupure : remontee a la vitesse de sa cause
                _dureeFondu = DureeRetour(_derniereRaison);
                _derniereRaison = RaisonAucune;
            }
            else
            {
                // simple variation d'attenuation : descendre vite, remonter
                // lentement, pour que le retour apres une replique ne s'entende
                // pas comme un a-coup
                _dureeFondu = baisser ? FonduAttenuation : FonduRetourAttenuation;
            }

            if (_diag && baisser != _baissaitAvant)
            {
                _baissaitAvant = baisser;
                Journal.Ecrire("[audio] attenuation " + (baisser ? "activee" : "levee")
                               + "   mission=" + Natif.EnMission()
                               + " dialogue=" + Natif.DialogueEnCours());
            }
            if (_diag && raison != _raisonTracee)
            {
                _raisonTracee = raison;
                Journal.Ecrire("[audio] cause = " + NomRaison(raison));
            }

            Avancer();
            Tracer();
        }

        /// <summary>
        /// Ce qu'il reste du volume pendant une mission ou un dialogue.
        /// 0,30 = soixante-dix pour cent de moins.
        /// </summary>
        private float _attenuation = 0.30f;
        private bool _repliquesAmbiantes;
        private bool _baissaitAvant;

        private int _raisonTracee = -1;

        private static string NomRaison(int r)
        {
            switch (r)
            {
                case RaisonPause: return "menu pause";
                case RaisonFocus: return "fenetre en arriere-plan";
                case RaisonCinematique: return "cinematique";
                case RaisonHorsVehicule: return "hors vehicule";
                case RaisonStation: return "changement de station";
                case RaisonEcranNoir: return "ecran noir";
                case RaisonHorsJeu: return "mort ou arrestation";
                case RaisonBascule: return "changement de personnage";
                case RaisonVehicule: return "vehicule inapte";
                default: return "aucune, le son joue";
            }
        }

        private const int FonduAttenuation = 420;        // on s'efface
        private const int FonduRetourAttenuation = 760;  // on revient, plus doucement

        // Raisons de couper, de la plus imperative a la plus douce.
        private const int RaisonAucune = 0;
        private const int RaisonPause = 1;
        private const int RaisonFocus = 2;
        private const int RaisonCinematique = 3;
        private const int RaisonHorsVehicule = 4;
        private const int RaisonStation = 6;
        private const int RaisonEcranNoir = 7;    // chargement, teleportation, fondu
        private const int RaisonHorsJeu = 8;      // mort ou arrestation
        private const int RaisonBascule = 9;      // changement de personnage
        private const int RaisonVehicule = 10;    // moteur coupe, velo, epave, eau

        private int _derniereRaison = RaisonAucune;
        private bool _fondu;

        /// <summary>Duree de la descente, en millisecondes.</summary>
        private static int Duree(int raison)
        {
            switch (raison)
            {
                case RaisonPause: return 0;          // net : rien par-dessus le menu
                case RaisonFocus: return 130;
                case RaisonCinematique: return 300;
                case RaisonEcranNoir: return 200;
                case RaisonHorsJeu: return 260;
                case RaisonBascule: return 220;
                case RaisonVehicule: return 320;
                default: return 260;
            }
        }

        /// <summary>
        /// Duree de la remontee. Toujours plus lente que la descente : une
        /// radio qui revient d'un coup apres un dialogue s'entend comme une
        /// faute, alors qu'une qui revient doucement passe inapercue.
        /// </summary>
        private static int DureeRetour(int raison)
        {
            switch (raison)
            {
                case RaisonPause: return 220;
                case RaisonFocus: return 200;
                case RaisonCinematique: return 620;
                case RaisonEcranNoir: return 420;
                case RaisonHorsJeu: return 720;     // on revient de loin
                case RaisonBascule: return 540;
                case RaisonVehicule: return 420;
                default: return 320;
            }
        }

        /// <summary>
        /// Amorce un fondu de changement de station. La station suivante
        /// arrivera en montant depuis zero, au lieu de surgir a plein volume.
        /// </summary>
        internal void Fondre()
        {
            _fondu = true;
        }

        /// <summary>
        /// Arme le verrou et coupe sans rampe. Appele des l'appui sur la touche
        /// pause, avant meme que le menu soit affiche.
        /// </summary>
        internal void ArmerVerrouPause()
        {
            if (_verrouPause) return;
            _verrouPause = true;
            _gainCible = 0f;
            _gainActuel = 0f;
            _dureeFondu = 0;
            _derniereRaison = RaisonPause;
            _lecteur.DefinirGain(0f);
            if (_diag) Journal.Ecrire("[PauseDiag] verrou arme sur appui touche");
        }

        /// <summary>
        /// Rampe fondee sur le TEMPS ecoule, pas sur le nombre d'images : sinon
        /// la duree d'un fondu dependrait du nombre d'images par seconde, et
        /// changerait avec le ralenti de la roue.
        /// </summary>
        private void Avancer()
        {
            int maintenant = Environment.TickCount;
            int dt = maintenant - _instantRampe;
            _instantRampe = maintenant;
            if (dt < 0 || dt > 500) dt = 16;        // reprise apres un gel

            if (_gainActuel == _gainCible) return;

            if (_dureeFondu <= 0)
            {
                _gainActuel = _gainCible;
            }
            else
            {
                float pas = (float)dt / _dureeFondu;
                if (_gainActuel < _gainCible) _gainActuel = Math.Min(_gainCible, _gainActuel + pas);
                else _gainActuel = Math.Max(_gainCible, _gainActuel - pas);
            }
            _lecteur.DefinirGain(_gainActuel);
        }

        private int _instantRampe;
        private int _dureeFondu = 260;

        // ------------------------------------------------------------------

        /// <summary>
        /// Le verrou ne se leve que lorsque le jeu confirme la fermeture du
        /// menu. Au retour, le volume est relu : on ne restaure jamais 1.0,
        /// sinon changer le curseur pendant la pause serait sans effet.
        /// </summary>
        /// <summary>
        /// Le verrou est COLLANT. Il ne se recalcule pas a chaque image a
        /// partir d'un signal du jeu : deux tentatives l'ont montre, aucune des
        /// natives prevues pour cela ne repond sur Enhanced, et l'horloge du
        /// jeu ne s'arrete pas davantage. Le journal d'une session complete le
        /// prouve : la ligne d'etat n'a jamais change, menu ouvert cinq fois.
        ///
        /// On s'appuie donc sur ce qui est observable avec certitude :
        ///   - la touche pause est vue par le mod (prouve par le journal) ;
        ///   - pendant que le menu a le focus, le jeu neutralise les commandes
        ///     de jeu, donc en voir une active signifie qu'on est revenu.
        ///
        /// Si l'un des signaux du jeu finit par repondre, il est pris en compte
        /// en plus : la sonde ci-dessous sert a l'identifier.
        /// </summary>
        private void LireMenuPause()
        {
            _pause.Rafraichir();

            // signaux du jeu, s'ils daignent repondre un jour
            // UNIQUEMENT le menu pause. Les cinematiques sont lues en
            // continu ailleurs : les melanger ici les rendait collantes.
            bool signal = _pause.Fige || Natif.MenuPauseOuvert();
            if (signal) _verrouPause = true;

            // Retour au jeu, deux preuves acceptees :
            //   - le script vient de recommencer a tourner apres un gel ;
            //   - une commande de jeu redevient active.
            if (_verrouPause && !signal
                && (_pause.VientDeReprendre || Natif.JoueurAgit()))
            {
                _verrouPause = false;
                _compteurLecture = 0;             // relecture immediate du volume
                if (_diag) Journal.Ecrire("[PauseDiag] verrou leve   ("
                    + (_pause.VientDeReprendre ? "reprise du script" : "commande de jeu")
                    + ")");
            }

            if (_verrouPause != _dernierePause)
            {
                _dernierePause = _verrouPause;
                if (_diag) Journal.Ecrire("[PauseDiag] verrou = " + _verrouPause
                                          + "   |  " + Natif.SondePause());
            }
            Sonder();
        }

        /// <summary>
        /// Sonde continue, toutes les 500 ms, pour identifier enfin quel signal
        /// du jeu change reellement quand le menu pause s'ouvre. Ne s'ecrit que
        /// si la reponse change, donc quelques lignes par session.
        /// </summary>
        private void Sonder()
        {
            if (!_diag) return;
            int maintenant = Environment.TickCount;
            if (maintenant - _derniereSonde < 500) return;
            _derniereSonde = maintenant;

            // un reglage du jeu a-t-il bouge : c est ainsi qu on identifiera
            // celui qui correspond vraiment au curseur Musique
            string bouge = Natif.ReglagesQuiChangent();
            if (bouge != null) Journal.Ecrire("[Reglages] " + bouge);

            string etat = Natif.SondePause();
            // l horloge change en permanence : on la retire de la comparaison
            int coupe = etat.IndexOf("  horlogeJeu=", StringComparison.Ordinal);
            string cle = coupe > 0 ? etat.Substring(0, coupe) : etat;
            if (cle == _derniereSondeTexte) return;

            _derniereSondeTexte = cle;
            Journal.Ecrire("[Sonde] " + etat);
        }

        /// <summary>
        /// Relu plusieurs fois par seconde, pas seulement au lancement d'une
        /// station : le joueur peut changer le curseur depuis le menu pause, et
        /// le nouveau niveau doit s'appliquer des la fermeture.
        /// </summary>
        private void LireVolumeJeu()
        {
            if (!_suivreJeu) { _volumeJeu = 1f; return; }

            if (--_compteurLecture > 0) return;
            _compteurLecture = 10;                 // environ 6 fois par seconde

            int slider = Natif.VolumeMusiqueBrut();
            int profil = Natif.ReglageBrut(Natif.ReglageVolumeMusique);

            if (_diag && (slider != _dernierSlider || profil != _dernierProfil))
            {
                _dernierSlider = slider;
                _dernierProfil = profil;
                Journal.Ecrire("[AudioDiag] GET_MUSIC_VOL_SLIDER = " + slider
                               + "   PROFILE_306 = " + profil);
            }

            float v = Natif.Normaliser(slider);
            if (v < 0f) v = Natif.Normaliser(profil);      // recours
            if (v < 0f) { _volumeJeu = 1f; return; }       // rien de lisible

            _volumeJeu = v;
        }

        /// <summary>Trace du gain reellement applique, au plus 4 fois par seconde.</summary>
        private void Tracer()
        {
            if (!_diag) return;
            if (Math.Abs(_gainActuel - _dernierGainTrace) < 0.01f) return;

            int maintenant = Environment.TickCount;
            if (maintenant - _derniereTrace < 250) return;
            _derniereTrace = maintenant;
            _dernierGainTrace = _gainActuel;

            Journal.Ecrire("[AudioDiag] brut = " + _dernierSlider
                           + "   normalise = " + _volumeJeu.ToString("0.00")
                           + "   verrou pause = " + _verrouPause
                           + "   gain effectif = " + _gainCible.ToString("0.00")
                           + "   VolumeSampleProvider.Volume = " + _gainActuel.ToString("0.00"));
        }
    }

    /// <summary>
    /// GTA.Game.IsPaused peut lever si l'etat du jeu n'est pas pret ; on
    /// l'isole pour que la lecture ne fasse jamais remonter d'exception.
    /// </summary>
    internal static class EtatJeu
    {
        internal static bool EnPause()
        {
            try { return GTA.Game.IsPaused; }
            catch { return false; }
        }
    }
}
