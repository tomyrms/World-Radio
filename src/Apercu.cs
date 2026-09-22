// ============================================================================
//  Apercu  -  "en cours de lecture", en bas a droite
// ----------------------------------------------------------------------------
//  Tres compact : un voile sombre tres leger, et rien d'autre. Pas de
//  bordure, pas d'en-tete, aucune mention "RADIO" ni "NOW PLAYING". L'ombre
//  portee du texte fait le reste.
//
//  Hierarchie : le titre domine, l'artiste vient en retrait, la station est
//  la plus discrete.
//
//  Trois modes : Auto, qui le montre quelques secondes a chaque changement,
//  Toujours, et Desactive.
//
//  La pochette a ete retiree : ScriptHookV a fait tomber le jeu sur un fichier
//  servi en PNG alors que son URL annoncait .jpg.
//      FATAL: directx texture ...\cache\1021854669.jpg creation failed
//  On n'affiche donc que le logo de la station, qui est un fichier local dont
//  on maitrise le format.
// ============================================================================

using System;
using System.Drawing;
using GTA.UI;

namespace WorldRadio
{
    internal enum ModeApercu { Auto, Toujours, Desactive }

    internal sealed class Apercu
    {
        private const float L = 346f;          // largeur, en unites de dessin
        private const float H = 74f;
        private const float Logo = 46f;
        private const int DureeFondu = 170;    // ms
        private const int DureeCrossfade = 240;
        private const float Glissement = 22f;  // arrivee par la droite

        private int _debutAffichage = int.MinValue;
        private int _debutMorceau = int.MinValue;
        private string _artiste = "";
        private string _titre = "";
        private string _artistePrecedent = "";
        private string _titrePrecedent = "";

        internal void Montrer()
        {
            _debutAffichage = Environment.TickCount;
        }

        /// <summary>
        /// Efface l apercu sans toucher au reste. Appele des que le joueur
        /// quitte son vehicule : la radio se tait, l encart ne doit donc pas
        /// surgir au changement de morceau alors qu on est a pied.
        /// </summary>
        internal void Masquer()
        {
            _debutAffichage = int.MinValue;
        }

        internal void Reinitialiser()
        {
            _debutAffichage = int.MinValue;
            _debutMorceau = int.MinValue;
            _artiste = ""; _titre = "";
            _artistePrecedent = ""; _titrePrecedent = "";
            _debutVolume = int.MinValue;
            _connexion = false;
            _injoignable = false;
        }

        // ------------------------------------------------------------------

        internal void Dessiner(Config c, Station station, Pays pays, Metadonnees meta,
                               Spectre spectre)
        {
            if (station == null || c.Apercu == ModeApercu.Desactive) return;

            string artiste = meta.Artiste;
            string titre = meta.Titre;

            // un nouveau morceau fait reapparaitre l'apercu et lance le fondu
            // croise entre l'ancien texte et le nouveau
            if (artiste != _artiste || titre != _titre)
            {
                _artistePrecedent = _artiste;
                _titrePrecedent = _titre;
                _artiste = artiste;
                _titre = titre;
                _debutMorceau = Environment.TickCount;
                if (_debutAffichage != int.MinValue) Montrer();
            }

            int alpha = Alpha(c);
            if (alpha <= 0) return;

            try
            {
                float marge = Ecran.Marge;
                float p = Dessin.Progression(_debutAffichage, DureeFondu);
                float x = Ecran.Largeur - marge - L + (1f - p) * Glissement;
                float y = Bas(c);

                Fond(x, y, alpha);

                Dessin.ImageCentree(station.Icone, x + 6f + Logo / 2f, y + H / 2f, Logo, alpha);

                float tx = x + Logo + 14f;

                // Les barres occupent le coin haut droit : le texte s'arrete
                // avant, sinon un titre long passerait dessous.
                bool barres = spectre != null && !spectre.Muet;
                float dispo = L - Logo - 26f - (barres ? LargeurBarres + 8f : 0f);

                float q = Dessin.Progression(_debutMorceau, DureeCrossfade);
                int aNouveau = (int)(alpha * q);
                int aAncien = (int)(alpha * (1f - q));

                if (aAncien > 8 && !string.IsNullOrEmpty(_titrePrecedent))
                    Lignes(tx, y, dispo, _titrePrecedent, _artistePrecedent, aAncien);
                Lignes(tx, y, dispo, titre, artiste, aNouveau);

                // la station, la plus discrete des trois, precedee de son
                // drapeau : le lien avec le pays choisi reste visible
                float xs = tx;
                if (pays != null && !string.IsNullOrEmpty(pays.Drapeau))
                {
                    float ld, hd;
                    Dessin.Contenir(pays.Drapeau, 21f, out ld, out hd);
                    Dessin.ImageEtiree(pays.Drapeau, tx, y + H - 13f - hd / 2f,
                                       ld, hd, alpha * 180 / 255);
                    xs += ld + 5f;
                }
                Dessin.Texte(Dessin.Couper(station.Nom.ToUpperInvariant(),
                                           L - (xs - x) - 10f, 0.28f),
                             xs, y + H - 21f, 0.28f,
                             Color.FromArgb(alpha * 120 / 255, 198, 198, 198), Alignment.Left);

                if (barres) Barres(x + L - 12f - LargeurBarres, y + 25f, spectre, alpha);

                Jauge(x, y, alpha);
            }
            catch (Exception ex) { Journal.Erreur("dessin de l'apercu", ex); }
        }

        /// <summary>
        /// Un seul voile sombre, tres discret.
        ///
        /// La version precedente empilait sept bandes d'opacites croissantes
        /// pour simuler un degrade. A l'ecran cela ne donnait pas un degrade
        /// mais un escalier, avec des marches nettes : c'etait la principale
        /// laideur de l'ancien rendu. Un aplat leger, l'ombre portee du texte
        /// faisant le reste, est plus propre et plus sobre.
        /// </summary>
        private static void Fond(float x, float y, int alpha)
        {
            // Un degrade qui se dissout vers le centre de l'ecran, plutot qu'un
            // rectangle a bord net : l'encart se pose sur l'image au lieu d'y
            // decouper une boite. Le degrade est un fichier a nous, et non une
            // texture du jeu dont on devrait deviner l'orientation.
            Dessin.ImageEtiree("veil_fade.png", x, y, L, H, alpha * 120 / 255);
        }

        // ------------------------------------------------------------------
        //  Barres de niveau
        // ------------------------------------------------------------------
        private const float LargeurBarre = 4f;
        private const float EcartBarre = 2.5f;
        private const float HauteurBarre = 17f;
        private const float LargeurBarres =
            Spectre.Bandes * LargeurBarre + (Spectre.Bandes - 1) * EcartBarre;

        /// <summary>
        /// Cinq barres tirees des echantillons reellement joues. Elles disent
        /// aussi, d'un coup d'oeil, que le flux vit encore : plates alors que
        /// l'encart annonce "EN DIRECT", c'est que plus rien n'arrive.
        /// </summary>
        private static void Barres(float x, float bas, Spectre spectre, int alpha)
        {
            for (int i = 0; i < Spectre.Bandes; i++)
            {
                float n = spectre.Niveau(i);
                float h = 1.5f + n * (HauteurBarre - 1.5f);   // jamais tout a fait nulle
                float bx = x + i * (LargeurBarre + EcartBarre);

                // le socle reste visible, la partie vive s'eclaircit avec le niveau
                Dessin.VoileClair(bx, bas - HauteurBarre, LargeurBarre, HauteurBarre,
                                  alpha * 38 / 255);
                Dessin.VoileClair(bx, bas - h, LargeurBarre, h,
                                  alpha * (110 + (int)(125f * n)) / 255);
            }
        }

        // ------------------------------------------------------------------

        private static bool _connexion;
        private static bool _injoignable;

        internal static void Etat(bool connexion, bool injoignable)
        {
            _connexion = connexion;
            _injoignable = injoignable;
        }

        private static void Lignes(float x, float y, float dispo, string titre, string artiste, int a)
        {
            if (a <= 0) return;

            if (string.IsNullOrEmpty(titre))
            {
                // Tant qu aucun titre n est connu, on dit lequel des deux etats
                // on traverse : la connexion dure quelques secondes, et un
                // encart muet laisserait croire a une panne.
                string etat = _injoignable ? "STATION INJOIGNABLE"
                            : _connexion   ? "CONNEXION..."
                                           : "EN DIRECT";
                Color teinte = _injoignable ? Color.FromArgb(a * 200 / 255, 235, 150, 140)
                                            : Color.FromArgb(a * 180 / 255, 225, 225, 225);
                Dessin.Texte(etat, x, y + 14f, 0.38f, teinte, Alignment.Left);
                return;
            }

            Dessin.Texte(Dessin.Couper(titre, dispo, 0.46f), x, y + 4f, 0.46f,
                         Color.FromArgb(a, 255, 255, 255), Alignment.Left);

            if (!string.IsNullOrEmpty(artiste))
                Dessin.Texte(Dessin.Couper(artiste, dispo, 0.35f), x, y + 30f, 0.35f,
                             Color.FromArgb(a * 180 / 255, 200, 200, 200), Alignment.Left);
        }

        // ------------------------------------------------------------------
        //  Jauge de volume
        // ------------------------------------------------------------------
        private static int _debutVolume = int.MinValue;
        private static float _volume;

        // ------------------------------------------------------------------
        //  Vignettage
        // ------------------------------------------------------------------
        //  Le jeu assombrit les bords de l'ecran pendant sa roue des radios ;
        //  le mod qui supprime cet effet l'appelle d'ailleurs « vignetting ».
        //  On le reproduit avec notre propre image plutot que de parier sur le
        //  nom d'un effet interne dont rien ne garantit ce qu'il fait.
        //
        //  L'image monte et descend en fondu : apparaitre d'un bloc sauterait
        //  aux yeux bien plus que l'effet lui-meme.
        private static float _vignette;
        private static int _vignetteInstant;

        internal static void Vignettage(bool actif)
        {
            int maintenant = Environment.TickCount;
            int dt = maintenant - _vignetteInstant;
            _vignetteInstant = maintenant;
            if (dt < 0 || dt > 500) dt = 16;

            float vise = actif ? 1f : 0f;
            _vignette += (vise - _vignette) * (1f - (float)Math.Exp(-dt / 95.0));
            if (_vignette < 0.004f) { _vignette = 0f; return; }

            try
            {
                Dessin.ImageEtiree("vignette.png", 0f, 0f,
                                   Ecran.Largeur, Ecran.Hauteur,
                                   (int)(255f * _vignette));
            }
            catch (Exception ex) { Journal.Erreur("vignettage", ex); }
        }

        /// <summary>
        /// Haut de l'encart. On remonte du decalage configure : GTA annonce le
        /// quartier traverse dans ce meme coin, et l'encart lui passait dessus.
        /// </summary>
        private static float Bas(Config c)
        {
            return Ecran.Hauteur - Ecran.Marge - H - (float)c.HauteurApercu;
        }

        /// <summary>
        /// Jauge seule, sans l'encart. Le volume reste reglable a pied alors
        /// que la radio s'y tait : sans ce rappel, appuyer sur la touche ne
        /// produisait strictement aucun retour a l'ecran.
        /// </summary>
        internal static void DessinerJaugeSeule(Config c)
        {
            if (_debutVolume == int.MinValue || !Visible()) return;
            try
            {
                float x = Ecran.Largeur - Ecran.Marge - L;
                float y = Bas(c);

                // un fond minimal, juste derriere la barre, plutot que le
                // bandeau entier : rien d'autre n'a de sens a pied
                Dessin.Voile(x, y + H - 19f, L, 19f, 90);
                Dessin.Texte("VOLUME " + ((int)(_volume * 100f + 0.5f)) + " %",
                             x + 6f, y + H - 21f, 0.28f,
                             Color.FromArgb(150, 198, 198, 198), Alignment.Left);
                Jauge(x, y, 255);
            }
            catch (Exception ex) { Journal.Erreur("dessin de la jauge", ex); }
        }

        private static bool Visible()
        {
            int ecoule = Environment.TickCount - _debutVolume;
            return ecoule >= 0 && ecoule <= 2000;
        }

        /// <summary>Signale un changement de volume : la jauge parait 2 s.</summary>
        internal static void MontrerVolume(double v)
        {
            _volume = (float)v;
            _debutVolume = Environment.TickCount;
        }

        /// <summary>
        /// Fine barre au bas de l'encart, visible seulement dans les deux
        /// secondes suivant un reglage. Plus lisible qu'un message dans le fil
        /// de notifications du jeu, et cela n'encombre pas le reste du temps.
        /// </summary>
        private static void Jauge(float x, float y, int alpha)
        {
            if (_debutVolume == int.MinValue) return;
            int ecoule = Environment.TickCount - _debutVolume;
            if (ecoule < 0 || ecoule > 2000) return;

            int a = alpha;
            if (ecoule > 1700) a = alpha * (2000 - ecoule) / 300;   // fondu de sortie
            if (a <= 0) return;

            const float h = 2f;
            float yb = y + H - h;
            Dessin.Voile(x, yb, L, h, a * 70 / 255);
            Dessin.VoileClair(x, yb, L * _volume, h, a * 200 / 255);
        }

        /// <summary>
        /// Apparition, maintien, puis effacement. En mode Toujours, seule
        /// l'apparition joue.
        /// </summary>
        private int Alpha(Config c)
        {
            if (_debutAffichage == int.MinValue) return 0;

            int ecoule = Environment.TickCount - _debutAffichage;
            if (ecoule < 0) return 255;                    // TickCount a boucle
            if (ecoule < DureeFondu) return (int)(255f * ecoule / DureeFondu);

            if (c.Apercu == ModeApercu.Toujours) return 255;

            // Une connexion qui traine ou une station injoignable sont
            // precisement les moments ou le joueur a besoin de l'encart : il
            // reste affiche tant que la situation n'est pas resolue, au lieu
            // de s'effacer au bout de cinq secondes sur un silence inexplique.
            if (_connexion || _injoignable) return 255;

            int maintien = (int)(c.SecondesApercu * 1000);
            if (ecoule < maintien) return 255;

            int apres = ecoule - maintien;
            if (apres >= DureeFondu) return 0;
            return 255 - (int)(255f * apres / DureeFondu);
        }
    }
}
