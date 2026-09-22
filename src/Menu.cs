// ============================================================================
//  Menu  -  roue radiale, une par pays
// ----------------------------------------------------------------------------
//  Geste : on MAINTIENT la commande radio du jeu, on VISE au stick droit, on
//  relache pour fermer. Se greffer sur cette commande plutot que d'inventer un
//  bouton la rend disponible au clavier comme a la manette sans reglage.
//
//  On ne defile plus, on pointe. C'est ce que fait la roue d'origine de GTA, et
//  la difference n'est pas cosmetique : avec une liste il faut compter les
//  crans et regarder ce qui passe, alors qu'avec une roue chaque station garde
//  sa place. Au bout de quelques fois, le geste se fait sans lire.
//
//      stick vers un secteur   vise cette station
//      12 h, tout en haut      RADIO OFF, a la meme place dans tous les pays
//      R1                      pays suivant, en boucle
//
//  Et un APPUI BREF sur la meme touche, sans maintenir, coupe ou retablit la
//  radio : le geste qu'on fait le plus souvent merite le moins de gestes.
//      relacher                fermer
//
//  Le pays est une PAGE, pas un niveau : on ne rentre nulle part, on ne
//  ressort de nulle part. Il n'y a plus d'etat cache.
//
//  La commande est neutralisee cote jeu avant d'etre lue, sans quoi la roue de
//  GTA s'ouvrirait en meme temps que la notre.
// ============================================================================

using System;

namespace WorldRadio
{
    internal enum ActionMenu { Aucune, Eteindre, Station }

    internal sealed class Menu
    {
        /// <summary>
        /// Amplitude minimale du stick pour qu'une visee compte. En-deca, on
        /// garde la selection courante : relacher le pouce ne doit pas sauter
        /// sur un secteur au hasard.
        /// </summary>
        private const float SeuilVisee = 0.42f;

        /// <summary>
        /// En deca de ce temps de maintien, l'appui compte comme BREF : il
        /// bascule la radio au lieu d'ouvrir la roue. C'est le geste du
        /// Radioport de Cyberpunk, et c'est celui qu'on fait le plus souvent :
        /// couper le son sans quitter la route des yeux.
        /// </summary>
        private const int SeuilAppuiBref = 200;   // ms

        private int _instantAppui = int.MinValue;
        private bool _basculeDemandee;

        /// <summary>Un appui bref vient d'avoir lieu. La lecture la consomme.</summary>
        internal bool BasculeDemandee
        {
            get { bool b = _basculeDemandee; _basculeDemandee = false; return b; }
        }

        private bool _ouvert;
        private int _pays;             // index dans Config.Pays
        private int _secteur;          // 0 = RADIO OFF, 1..n = stations
        private bool _armePage = true;
        private bool _aInteragi;

        private float _angleStick;     // radians, 0 en haut, sens horaire
        private bool _viseeActive;

        // Derniere cible reellement annoncee : -1 pour RADIO OFF, sinon
        // l'index de la station. On compare CELA, et non le numero de
        // secteur : changer de pays garde souvent le meme numero tout en
        // pointant une autre station, qui ne serait alors jamais lancee.
        private int _derniereCible = int.MinValue;

        internal bool Ouvert { get { return _ouvert; } }
        internal int PaysCourant { get { return _pays; } }

        /// <summary>Secteur vise : 0 pour RADIO OFF, sinon la station numero secteur-1.</summary>
        internal int Secteur { get { return _secteur; } }

        /// <summary>Direction du stick, pour dessiner l'aiguille. NaN si au repos.</summary>
        internal float AngleStick { get { return _viseeActive ? _angleStick : float.NaN; } }

        // ------------------------------------------------------------------

        internal ActionMenu MettreAJour(Config c, int repere, int enCours, out int indexStation)
        {
            indexStation = -1;
            if (c.Pays.Count == 0) { Fermer(); return ActionMenu.Aucune; }

            // A pied, pas de roue : la roue des radios de GTA ne s'ouvre pas
            // davantage hors d'un vehicule. Elle se ferme aussi si on quitte
            // la voiture alors qu'elle etait ouverte.
            if (!Natif.DansVehicule()) { Fermer(); return ActionMenu.Aucune; }

            Natif.DesactiverCommande(GTA.Control.VehicleRadioWheel);
            bool maintenu = Natif.CommandeEnfoncee(GTA.Control.VehicleRadioWheel);

            if (!maintenu)
            {
                // relachee vite : c'etait une bascule, pas une ouverture
                if (_instantAppui != int.MinValue)
                {
                    if (Environment.TickCount - _instantAppui < SeuilAppuiBref) _basculeDemandee = true;
                    _instantAppui = int.MinValue;
                }
                Fermer();
                return ActionMenu.Aucune;
            }

            if (_instantAppui == int.MinValue) _instantAppui = Environment.TickCount;

            // La roue ne s'ouvre qu'une fois le seuil franchi : sinon un simple
            // appui la ferait clignoter a l'ecran.
            if (Environment.TickCount - _instantAppui < SeuilAppuiBref) return ActionMenu.Aucune;

            if (!_ouvert)
            {
                _ouvert = true;
                _armePage = true;
                _viseeActive = false;
                _aInteragi = false;
                PositionnerSur(c, repere, enCours);
            }

            // tant que notre roue est ouverte, le jeu ne doit reagir ni aux
            // axes ni aux boutons qu'on detourne
            Natif.DesactiverCommande(GTA.Control.RadioWheelUpDown);
            Natif.DesactiverCommande(GTA.Control.RadioWheelLeftRight);
            Natif.DesactiverCommande(GTA.Control.VehicleHandbrake);

            if (c.JournalAudio) Natif.TracerBoutonsRoue();

            if (ChangerDePays(c)) _aInteragi = true;
            if (Viser(c)) _aInteragi = true;

            // Tant que le joueur n'a rien fait, la roue ne decide rien. Sans
            // cela, l'ouvrir suffirait a relancer la station sur laquelle le
            // curseur s'est pose.
            if (!_aInteragi) return ActionMenu.Aucune;

            int cible = Cible(c);
            if (cible == int.MinValue || cible == _derniereCible) return ActionMenu.Aucune;
            _derniereCible = cible;

            // La visee s'applique AUSSITOT : on entend ce qu'on pointe, comme
            // sur la roue d'origine. Pousser une seconde fois pour valider
            // n'aurait servi a rien.
            if (cible < 0) return ActionMenu.Eteindre;

            indexStation = cible;
            return ActionMenu.Station;
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Ce que vise le secteur courant : -1 pour RADIO OFF, l'index de la
        /// station sinon, int.MinValue si le secteur ne designe rien.
        /// </summary>
        private int Cible(Config c)
        {
            if (_secteur == 0) return -1;
            if (_pays < 0 || _pays >= c.Pays.Count) return int.MinValue;

            Pays p = c.Pays[_pays];
            int i = _secteur - 1;
            if (i < 0 || i >= p.Stations.Count) return int.MinValue;
            return p.Stations[i].Index;
        }

        /// <summary>Nombre de secteurs du pays courant : RADIO OFF plus ses stations.</summary>
        internal static int Secteurs(Config c, int pays)
        {
            if (pays < 0 || pays >= c.Pays.Count) return 1;
            return c.Pays[pays].Stations.Count + 1;
        }

        /// <summary>
        /// Angle du centre d'un secteur, en radians, 0 en haut et sens horaire.
        /// RADIO OFF occupe le secteur 0, donc toujours midi : la meme place
        /// dans tous les pays, donc un geste qu'on finit par faire sans lire.
        /// </summary>
        internal static float AngleSecteur(int secteur, int total)
        {
            if (total <= 0) return 0f;
            return (float)(2.0 * Math.PI * secteur / total);
        }

        // ------------------------------------------------------------------

        private bool Viser(Config c)
        {
            float x = Natif.ValeurAxe(GTA.Control.RadioWheelLeftRight);
            float y = Natif.ValeurAxe(GTA.Control.RadioWheelUpDown);
            if (c.AxeMenuInverse) y = -y;

            float force = (float)Math.Sqrt(x * x + y * y);
            if (force < SeuilVisee) { _viseeActive = false; return false; }

            // atan2(x, -y) : l'axe vertical du jeu est positif vers le bas, on
            // veut 0 en haut et les angles qui croissent dans le sens horaire.
            float angle = (float)Math.Atan2(x, -y);
            if (angle < 0f) angle += (float)(2.0 * Math.PI);

            _angleStick = angle;
            _viseeActive = true;
            _secteur = SecteurPour(angle, Secteurs(c, _pays));
            return true;
        }

        /// <summary>
        /// Secteur designe par un angle. Sorti a part pour etre verifiable sans
        /// le jeu : c'est le coeur du geste, une erreur ici enverrait le stick
        /// sur la mauvaise station sans que rien ne le signale.
        /// </summary>
        internal static int SecteurPour(float angle, int total)
        {
            if (total <= 0) return 0;

            float tour = (float)(2.0 * Math.PI);
            angle = angle % tour;
            if (angle < 0f) angle += tour;

            float pas = tour / total;

            // +0,5 pas : on arrondit au secteur le plus proche, le centre d'un
            // secteur etant au milieu de sa part de cercle.
            int secteur = (int)Math.Floor((angle + pas * 0.5f) / pas);
            return ((secteur % total) + total) % total;
        }

        /// <summary>
        /// Pays suivant, en boucle. UN SEUL bouton : le journal de jeu n'a
        /// jamais vu remonter autre chose que VehicleHandbrake, c'est-a-dire
        /// R1 sur la manette. Lier un second bouton au hasard aurait ajoute
        /// une commande qui ne repond pas.
        ///
        /// Le secteur retombe sur RADIO OFF quand la nouvelle page est plus
        /// courte, plutot que de pointer dans le vide.
        /// </summary>
        private bool ChangerDePays(Config c)
        {
            if (!Natif.CommandeEnfoncee(GTA.Control.VehicleHandbrake))
            {
                _armePage = true;
                return false;
            }
            if (!_armePage) return false;
            _armePage = false;

            int n = c.Pays.Count;
            _pays = (_pays + 1) % n;

            int total = Secteurs(c, _pays);
            if (_secteur >= total) _secteur = 0;
            return true;
        }

        /// <summary>
        /// A l'ouverture, le curseur se pose sur la derniere station ecoutee,
        /// mais la CIBLE de depart est ce qui joue reellement.
        ///
        /// Confondre les deux etait un piege : radio eteinte, le curseur se
        /// posait sur Skyrock et le menu enregistrait Skyrock comme cible
        /// courante. Viser Skyrock revenait alors a « tu y es deja », et la
        /// station ne repartait pas. Il fallait passer par une autre pour
        /// casser l'egalite.
        /// </summary>
        private void PositionnerSur(Config c, int repere, int enCours)
        {
            _pays = 0;
            _secteur = 0;
            _derniereCible = (enCours >= 0 && enCours < c.Stations.Count) ? enCours : -1;

            if (repere < 0 || repere >= c.Stations.Count) return;

            Station active = c.Stations[repere];
            for (int i = 0; i < c.Pays.Count; i++)
            {
                int j = c.Pays[i].Stations.IndexOf(active);
                if (j < 0) continue;
                _pays = i;
                _secteur = j + 1;          // decale de un, RADIO OFF occupant midi
                return;
            }
        }

        internal void Fermer()
        {
            _ouvert = false;
            _armePage = true;
            _viseeActive = false;
            _aInteragi = false;

            // L'horodatage de l'appui DOIT repartir de zero ici. Sans cela, une
            // sortie anticipee (a pied, configuration vide) laissait une date
            // perimee : l'appui suivant paraissait alors tres long, la roue
            // s'ouvrait aussitot, et la bascule ne partait plus jamais.
            _instantAppui = int.MinValue;
        }
    }
}
