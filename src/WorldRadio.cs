// ============================================================================
//  RADIO LIBRE  -  stations internet pour GTA V Enhanced
// ----------------------------------------------------------------------------
//  Mod autonome, sans dependance a un autre mod de radio.
//
//  Le son sort par une sortie audio propre au mod : ScriptHookVDotNet ne donne
//  aucun acces au mixeur du jeu, et aucun mod de radio en script ne fait
//  autrement. Le mod prend donc a sa charge tout ce que Rockstar ferait :
//
//      - volume asservi au curseur "Musique" des reglages audio du jeu
//      - silence immediat des l'ouverture du menu pause
//      - silence a pied et en arriere-plan
//      - extinction de la radio du vehicule pendant l'ecoute
//
//  Le script se declare NON PAUSABLE au demarrage. Sans cela GTA le suspend
//  des l'ouverture du menu pause : le tick ne tourne plus, et le mod n'a donc
//  aucune occasion de baisser son propre volume. C'etait la cause du son qui
//  continuait pendant la pause, qu'aucune detection n'aurait pu corriger.
//
//  Responsabilites separees :
//      Config           lecture du .ini
//      Lecteur          flux reseau ; applique un gain qu'on lui donne
//      ControleurAudio  SEULE autorite du volume
//      Metadonnees      artiste et titre, hors du fil du jeu
//      Menu             etat de la selection
//      Selecteur        rendu central pendant la selection
//      Apercu           rendu discret en bas a droite le reste du temps
//
//  Cycle des stations au clavier : station 1 -> 2 -> ... -> eteint -> 1
// ============================================================================

using System;
using GTA;
using GTA.UI;
using KeyEventArgs = System.Windows.Forms.KeyEventArgs;

namespace WorldRadio
{
    public class WorldRadioScript : Script
    {
        internal const string Version = "2.0.0";

        private Config _config;
        private readonly Menu _menu = new Menu();
        private readonly Selecteur _selecteur = new Selecteur();
        private readonly Apercu _apercu = new Apercu();
        private readonly Metadonnees _metadonnees = new Metadonnees();
        private readonly Spectre _spectre = new Spectre();

        // Construits dans le corps du constructeur, jamais en initialiseur de
        // champ : Lecteur reference des types NAudio, et le JIT les resoudrait
        // avant que le recours de resolution soit en place.
        private Lecteur _lecteur;
        private ControleurAudio _audio;

        /// <summary>Index de la station ; egal au nombre de stations = eteint.</summary>
        private int _index;

        private int _compteurSurveillance;
        private int _handleRadio;
        private int _compteurRadio;
        private bool _annonceFaite;
        private bool _menuOuvertAvant;
        private bool _enVehiculeAvant;

        // ------------------------------------------------------------------

        public WorldRadioScript()
        {
            try
            {
                string dossier = Dossiers.Scripts();
                Journal.Ouvrir(dossier);
                Natif.JournaliserEnvironnement(Version);
                ChargeurNAudio.Init(dossier);

                _lecteur = new Lecteur();
                _audio = new ControleurAudio(_lecteur);

                _config = Config.Charger(dossier);

                _index = _config.Stations.Count;          // eteint au demarrage

                _audio.Init(_config);
                Dessin.Init(_config);
                _metadonnees.Init(_config);

                // avant tout le reste : sans cela le tick s'arrete des
                // l'ouverture du menu pause
                Natif.NePasMettreEnPause();

                KeyDown += SurTouche;
                Tick += SurTick;
                Aborted += SurArret;

                Journal.Ecrire("pret : " + _config.Stations.Count + " station(s) dans "
                               + _config.Pays.Count + " pays");
            }
            catch (Exception ex)
            {
                Journal.Erreur("demarrage", ex);
            }
        }

        // SHVDN recharge les scripts a la touche Inser. Sans cette liberation,
        // l'ancien flux continuerait de jouer sans plus personne pour l'arreter,
        // et la radio du vehicule resterait eteinte.
        private void SurArret(object envoyeur, EventArgs e)
        {
            try
            {
                // Eteindre, pas Arreter : on met aussi fin aux fils de fond.
                // Arreter se contentait de couper le son, en laissant vivre
                // l'ouvrier de connexion et le fil des metadonnees ; apres un
                // rechargement ils auraient tourne en double.
                if (_lecteur != null) _lecteur.Eteindre();
                _metadonnees.Eteindre();
                if (_audio != null) _audio.Eteindre();

                // Ne JAMAIS laisser le jeu au ralenti derriere soi : un script
                // recharge alors que la roue etait ouverte figerait la partie
                // sans que rien n'indique pourquoi.
                RendreLeTemps();

                if (_handleRadio != 0)
                {
                    Natif.AllumerRadioVehicule(_handleRadio);
                    Natif.RadioMobile(true);
                }
                Journal.Ecrire("--- arret ---");
            }
            catch (Exception ex) { Journal.Erreur("arret", ex); }
        }

        // ------------------------------------------------------------------

        private void SurTick(object envoyeur, EventArgs e)
        {
            // le constructeur a pu echouer : on ne noie pas le journal d'une
            // exception par image
            if (_config == null || _lecteur == null) return;

            try
            {
                Annoncer();

                // Le joueur vient d'appuyer sur Start ou Echap : on coupe sans
                // attendre que le menu soit reellement ouvert, sinon la radio
                // reste audible une fraction de seconde par-dessus le menu.
                if (Natif.PauseVientDEtreDemandee()) _audio.ArmerVerrouPause();

                // une seule autorite decide du gain
                _audio.Rafraichir(!EnVehicule());

                GererRadioJeu();
                GererSelecteur();

                // Les niveaux se calculent sur le fil du jeu, a partir du
                // tampon rempli par le fil audio. Audible seulement : coupe,
                // les barres retombent au lieu de rester figees sur leur
                // derniere valeur, qui ferait croire a un son present.
                _spectre.Rafraichir(_lecteur.Sonde, _lecteur.Frequence,
                                    _audio.GainActuel > 0.001f && StationCourante() != null);

                if (++_compteurSurveillance >= 60)
                {
                    _compteurSurveillance = 0;
                    _lecteur.Surveiller();
                }

                Dessiner();
            }
            catch (Exception ex)
            {
                Journal.Erreur("tick", ex);
            }
        }

        // ------------------------------------------------------------------

        private void GererSelecteur()
        {
            int choix;
            // Quand rien ne joue, le menu s ouvre sur la derniere station
            // ecoutee plutot qu en haut de liste : une seule poussee suffit
            // a la reprendre.
            //  repere  : ou poser le curseur, la derniere station ecoutee
            //  enCours : ce qui joue VRAIMENT, ou -1 si la radio est eteinte
            int repere = _index < _config.Stations.Count ? _index : _config.DerniereStation;
            int enCours = _index < _config.Stations.Count ? _index : -1;
            ActionMenu action = _menu.MettreAJour(_config, repere, enCours, out choix);

            // L extinction emprunte la meme temporisation : traverser
            // "Radio Off" en defilant ne doit pas couper le son au passage.
            if (action == ActionMenu.Eteindre) Differer(_config.Stations.Count);
            else if (action == ActionMenu.Station) Differer(choix);

            AppliquerDiffere();

            // Appui bref sur la touche radio : on coupe ou on retablit, sans
            // ouvrir la roue. Seulement au volant, comme le reste.
            if (_menu.BasculeDemandee && EnVehicule()) BasculerRadio();

            bool ouvert = _menu.Ouvert;
            if (ouvert)
            {
                _selecteur.Anime(_menu.PaysCourant, _menu.Secteur);
            }
            else if (_menuOuvertAvant)
            {
                // a la fermeture, la roue disparait et l'apercu rappelle
                // brievement ce qui joue
                _selecteur.Reinitialiser();
                if (StationCourante() != null) _apercu.Montrer();
            }
            _menuOuvertAvant = ouvert;

            ReglerLeTemps(ouvert);
        }

        // Survoler les stations en changerait une par cran de stick, donc une
        // connexion reseau par cran. On attend que la selection se pose avant
        // d'ouvrir quoi que ce soit : le survol reste immediat a l'ecran, seul
        // le son attend un instant.
        private const int DelaiAvantLecture = 260;   // ms
        private int _attente = -1;
        private int _quandAppliquer;

        private void Differer(int index)
        {
            if (index == _attente) return;
            _attente = index;
            _quandAppliquer = Environment.TickCount + DelaiAvantLecture;
        }

        private void AppliquerDiffere()
        {
            if (_attente < 0) return;
            if (Environment.TickCount < _quandAppliquer) return;

            int index = _attente;
            _attente = -1;
            Selectionner(index);
        }

        /// <summary>
        /// Un seul des deux rendus a la fois : jamais le selecteur central et
        /// l'apercu simultanement.
        /// </summary>
        private void Dessiner()
        {
            if (_menu.Ouvert)
            {
                // Le vignettage du jeu pendant sa roue des radios : les bords
                // s'assombrissent et l'oeil va au centre. C'est lui, bien plus
                // que le ralenti seul, qui fait qu'on sent le jeu se mettre en
                // retrait. Dessine avant la roue, donc derriere elle.
                Apercu.Vignettage(_menu.Ouvert);
                _selecteur.Dessiner(_config, _menu);
                return;
            }
            Apercu.Vignettage(false);

            // A pied la radio se tait : l encart ne doit donc pas surgir au
            // changement de morceau. On le masque plutot que de le laisser
            // reapparaitre a chaque nouveau titre.
            bool dedans = EnVehicule();
            if (dedans && !_enVehiculeAvant && StationCourante() != null)
            {
                // en montant en voiture, un rappel discret de ce qui joue
                _apercu.Montrer();
            }
            _enVehiculeAvant = dedans;

            if (!dedans)
            {
                // A pied l'encart disparait, mais le volume reste reglable :
                // la jauge seule donne le retour qui manquait.
                _apercu.Masquer();
                Apercu.DessinerJaugeSeule(_config);
                return;
            }

            Apercu.Etat(_lecteur.EnConnexion, _lecteur.Injoignable);
            _apercu.Dessiner(_config, StationCourante(), PaysCourant(), _metadonnees, _spectre);
        }

        private void Annoncer()
        {
            if (_annonceFaite) return;
            _annonceFaite = true;

            if (_config.Stations.Count == 0)
            {
                Notification.PostTicker("~r~World Radio~s~ : aucune station chargee", false);
                Notification.PostTicker("~y~verifie~s~ WorldRadio.ini", false);
            }
        }

        /// <summary>
        /// Eteint la radio du vehicule tant qu'une station joue, et la rend au
        /// joueur des qu'on s'eteint.
        ///
        /// L'extinction est REAPPLIQUEE periodiquement : le jeu rallume sa
        /// radio de lui-meme, notamment a l'entree dans un vehicule, et aucune
        /// API ne permet de lire son etat pour le verifier.
        /// </summary>
        private void GererRadioJeu()
        {
            if (!_config.CouperRadioJeu) return;

            try
            {
                Ped joueur = Game.Player.Character;
                Vehicle v = joueur == null ? null : joueur.CurrentVehicle;
                int handle = (v == null || !v.Exists()) ? 0 : v.Handle;
                bool voulu = StationCourante() != null && handle != 0;

                if (voulu)
                {
                    if (handle != _handleRadio) { _handleRadio = handle; _compteurRadio = 0; }
                    if (--_compteurRadio <= 0)
                    {
                        _compteurRadio = 30;
                        Natif.EteindreRadioVehicule(handle);
                        Natif.RadioMobile(false);
                    }
                    return;
                }

                if (_handleRadio != 0)
                {
                    Natif.AllumerRadioVehicule(_handleRadio);
                    Natif.RadioMobile(true);
                    _handleRadio = 0;
                }
            }
            catch (Exception ex) { Journal.Erreur("extinction de la radio du jeu", ex); }
        }


        private void SurTouche(object envoyeur, KeyEventArgs e)
        {
            if (_config == null || _lecteur == null) return;
            try
            {
                // Echap arme la coupure AVANT que le menu s'ouvre.
                //
                // La detection par commande GTA n'est vraie que sur l'image
                // exacte de l'appui, et le script cesse de tourner des que le
                // menu s'affiche : selon l'instant, on attrapait cette image
                // ou on la ratait, d'ou une pause qui ne coupait qu'une fois
                // sur deux. KeyDown est un hook systeme, il ne rate rien.
                if (e.KeyCode == System.Windows.Forms.Keys.Escape)
                {
                    _audio.ArmerVerrouPause();
                    return;
                }

                // Le pave numerique envoie NumPad9 quand le verrou est actif,
                // PageUp sinon : on accepte les deux, plus les touches + et -
                // du pave, pour que le reglage marche dans tous les cas.
                // changer de station demande d etre au volant, comme dans le
                // jeu ; le volume, lui, reste reglable a tout moment
                if (e.KeyCode == _config.ToucheSuivante) { if (EnVehicule()) Changer(+1); }
                else if (e.KeyCode == _config.TouchePrecedente) { if (EnVehicule()) Changer(-1); }
                else if (Monte(e.KeyCode)) ReglerVolume(+0.10);
                else if (Descend(e.KeyCode)) ReglerVolume(-0.10);
                else if (e.KeyCode == _config.ToucheCoupure) BasculerRadio();
                else return;

                // trace : si rien n'apparait ici quand tu appuies, c'est que la
                // touche n'arrive meme pas jusqu'au mod
                if (_config.JournalAudio) Journal.Ecrire("[touche] " + e.KeyCode);
            }
            catch (Exception ex) { Journal.Erreur("touche", ex); }
        }

        // ------------------------------------------------------------------

        private Station StationCourante()
        {
            if (_index < 0 || _index >= _config.Stations.Count) return null;
            return _config.Stations[_index];
        }

        private Pays PaysCourant()
        {
            Station s = StationCourante();
            if (s == null) return null;
            foreach (Pays p in _config.Pays)
                if (p.Nom.Equals(s.NomPays, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        /// <summary>Fait tourner la selection, l'extinction etant une position a part entiere.</summary>
        private void Changer(int sens)
        {
            int total = _config.Stations.Count;
            if (total == 0)
            {
                Notification.PostTicker("~r~World Radio~s~ : aucune station", false);
                return;
            }
            int positions = total + 1;
            Selectionner(((_index + sens) % positions + positions) % positions);
        }

        /// <summary>
        /// Bascule sur une position. Ne fait rien si c'est deja la position
        /// courante : relacher le selecteur sans avoir bouge ne doit pas
        /// relancer la connexion.
        /// </summary>
        private void Selectionner(int index)
        {
            int total = _config.Stations.Count;
            if (total == 0) return;

            index = Math.Max(0, Math.Min(total, index));
            if (index == _index) return;

            Station avant = StationCourante();
            if (avant != null) Journal.Ecrire("[radio] arret " + avant.Nom);

            _index = index;
            Station s = StationCourante();

            // La station suivante montera depuis zero au lieu de surgir a
            // plein volume : c'est le controleur qui mene le fondu.
            _audio.Fondre();

            if (s == null)
            {
                _lecteur.Arreter();
                _metadonnees.Suivre(null);
                _apercu.Reinitialiser();
                _spectre.Reinitialiser();
                Journal.Ecrire("[radio] eteinte");
                return;
            }

            // le lecteur libere l'ancienne sortie avant d'ouvrir la nouvelle :
            // jamais deux flux a la fois
            _lecteur.Jouer(s.Url);
            _metadonnees.Suivre(s);
            _apercu.Montrer();
            _config.EnregistrerDerniereStation(index);
            Journal.Ecrire("[radio] demarrage " + s.Nom + "   metadonnees = " + s.Source);
        }

        /// <summary>
        /// Regle la PROPORTION du volume musique de GTA, pas un volume absolu.
        /// Le controleur reste seul a decider du gain reellement applique.
        /// </summary>
        private void ReglerVolume(double pas)
        {
            double v = Math.Round(Config.Borner(_config.Volume + pas), 2);
            if (Math.Abs(v - _config.Volume) < 0.001) return;

            _config.Volume = v;
            _audio.DefinirProportion(v);
            _config.EnregistrerVolume(v);
            Apercu.MontrerVolume(v);
            _apercu.Montrer();
        }

        private bool Monte(System.Windows.Forms.Keys k)
        {
            return k == _config.ToucheVolPlus
                || k == System.Windows.Forms.Keys.NumPad9
                || k == System.Windows.Forms.Keys.PageUp
                || k == System.Windows.Forms.Keys.Add;
        }

        private bool Descend(System.Windows.Forms.Keys k)
        {
            return k == _config.ToucheVolMoins
                || k == System.Windows.Forms.Keys.NumPad3
                || k == System.Windows.Forms.Keys.PageDown
                || k == System.Windows.Forms.Keys.Subtract;
        }

        /// <summary>
        /// Eteint la radio, ou la rallume sur la station d'avant.
        ///
        /// UN SEUL mecanisme d'extinction. Il y en avait deux auparavant : une
        /// « coupure » qui laissait la station selectionnee, et le secteur
        /// RADIO OFF de la roue. Deux etats pour une meme idee, donc des
        /// situations ou la radio paraissait eteinte sans l'etre vraiment.
        /// </summary>
        private int _avantExtinction = -1;

        private void BasculerRadio()
        {
            if (_config.Stations.Count == 0) return;

            if (StationCourante() != null)
            {
                _avantExtinction = _index;
                Selectionner(_config.Stations.Count);      // RADIO OFF
                return;
            }

            int retour = _avantExtinction;
            if (retour < 0 || retour >= _config.Stations.Count) retour = _config.DerniereStation;
            if (retour < 0 || retour >= _config.Stations.Count) retour = 0;
            Selectionner(retour);
        }

        // ------------------------------------------------------------------
        //  Ralenti
        // ------------------------------------------------------------------
        //  La premiere version basculait l'echelle du temps d'un coup, a
        //  l'ouverture comme a la fermeture. Ca ne ressemblait pas a l'effet du
        //  jeu, qui GLISSE vers le ralenti puis en ressort de meme.
        //
        //  L'interpolation se fait sur le temps REEL : Environment.TickCount
        //  n'est pas affecte par l'echelle du temps, contrairement a l'horloge
        //  du jeu, qui rendrait la sortie du ralenti interminable.
        private float _temps = 1f;
        private int _tempsInstant;

        private void ReglerLeTemps(bool roueOuverte)
        {
            if (_config == null || _config.Ralenti >= 1.0) return;

            int maintenant = Environment.TickCount;
            int dt = maintenant - _tempsInstant;
            _tempsInstant = maintenant;
            if (dt < 0 || dt > 500) dt = 16;

            float vise = roueOuverte ? (float)_config.Ralenti : 1f;
            if (Math.Abs(_temps - vise) < 0.004f)
            {
                if (_temps == vise) return;
                _temps = vise;
            }
            else
            {
                // 90 ms de constante de temps : assez rapide pour repondre au
                // geste, assez doux pour qu'on ne sente pas de marche
                _temps += (vise - _temps) * (1f - (float)Math.Exp(-dt / 90.0));
            }
            Natif.EchelleDuTemps(_temps);
        }

        /// <summary>Rend au jeu sa vitesse normale, sans transition.</summary>
        private void RendreLeTemps()
        {
            _temps = 1f;
            if (_config != null && _config.Ralenti >= 1.0) return;
            Natif.EchelleDuTemps(1f);
        }

        private static bool EnVehicule()
        {
            try
            {
                Ped joueur = Game.Player.Character;
                return joueur != null && joueur.IsInVehicle();
            }
            catch { return false; }
        }
    }
}
