// ============================================================================
//  Selecteur  -  la roue des stations du pays courant
// ----------------------------------------------------------------------------
//  Aucun fond, aucun cadre, aucune croix de fermeture : la roue se ferme en
//  relachant la touche radio. Rien que les logos disposes en cercle, leurs
//  noms, et le pays au centre. L'ombre portee du texte suffit a la lisibilite,
//  meme sur un ciel clair.
//
//  RADIO OFF occupe toujours midi, dans tous les pays : c'est ce qui permet de
//  couper la radio sans regarder.
//
//  ANIMATION CONTINUE. La premiere version faisait grandir d'un coup le
//  secteur vise et retomber les autres : a l'ecran, cela sautait. Ici, une
//  position flottante suit la visee en s'amortissant, et la taille comme la
//  teinte de CHAQUE secteur derivent de sa distance a cette position. Plus
//  rien ne saute, tout glisse.
//
//  L'amortissement est calcule sur le TEMPS ecoule, pas par image : sinon la
//  vitesse de l'animation dependrait du nombre d'images par seconde.
// ============================================================================

using System;
using System.Drawing;
using GTA.UI;

namespace WorldRadio
{
    internal sealed class Selecteur
    {
        private const int DureeApparition = 190;   // ms, ouverture de la roue
        private const int DureePage = 260;         // ms, changement de pays

        /// <summary>Constante de temps de l'amortissement, en ms.</summary>
        private const float Tau = 72f;

        private const float HauteurRelative = 0.395f;
        private const float Rayon = 132f;

        private const float LogoVise = 64f;
        private const float LogoAutre = 42f;

        private const float TexteVise = 0.42f;
        private const float TexteAutre = 0.29f;

        private int _debutApparition = int.MinValue;
        private int _debutPage = int.MinValue;
        private int _sensPage;
        private int _paysPrecedent = -1;

        // Position flottante de la visee, entre 0 et le nombre de secteurs.
        private float _visee;
        private bool _viseeAmorcee;
        private int _dernierInstant;

        internal void Anime(int pays, int secteur)
        {
            int maintenant = Environment.TickCount;
            if (_debutApparition == int.MinValue)
            {
                _debutApparition = maintenant;
                _dernierInstant = maintenant;
            }

            if (pays != _paysPrecedent)
            {
                if (_paysPrecedent >= 0)
                {
                    _sensPage = pays > _paysPrecedent ? 1 : -1;
                    _debutPage = maintenant;
                }
                _paysPrecedent = pays;
            }
        }

        internal void Reinitialiser()
        {
            _debutApparition = int.MinValue;
            _debutPage = int.MinValue;
            _paysPrecedent = -1;
            _viseeAmorcee = false;
        }

        private static float CentreY { get { return Ecran.Hauteur * HauteurRelative; } }

        // ------------------------------------------------------------------

        /// <summary>
        /// Rapproche la position flottante du secteur vise, par le plus court
        /// chemin SUR LE CERCLE : passer du dernier secteur au premier doit
        /// franchir midi, pas reculer sur tout le tour.
        /// </summary>
        private void Suivre(int secteur, int total)
        {
            int maintenant = Environment.TickCount;
            int dt = maintenant - _dernierInstant;
            _dernierInstant = maintenant;
            if (dt < 0 || dt > 500) dt = 16;          // reprise apres une pause

            if (!_viseeAmorcee) { _visee = secteur; _viseeAmorcee = true; return; }

            float ecart = secteur - _visee;
            while (ecart > total / 2f) ecart -= total;
            while (ecart < -total / 2f) ecart += total;

            float k = 1f - (float)Math.Exp(-dt / Tau);
            _visee += ecart * k;

            while (_visee < 0f) _visee += total;
            while (_visee >= total) _visee -= total;
        }

        /// <summary>Distance d'un secteur a la visee, sur le cercle.</summary>
        private float Distance(int secteur, int total)
        {
            float d = Math.Abs(secteur - _visee);
            if (d > total / 2f) d = total - d;
            return d;
        }

        // ------------------------------------------------------------------

        internal void Dessiner(Config c, Menu m)
        {
            try
            {
                if (m.PaysCourant < 0 || m.PaysCourant >= c.Pays.Count) return;

                int total = Menu.Secteurs(c, m.PaysCourant);
                Suivre(m.Secteur, total);

                float t = Dessin.Progression(_debutApparition, DureeApparition);
                float p = Dessin.Progression(_debutPage, DureePage);

                // Le pays entrant revient depuis le cote d'ou il vient, en se
                // reformant : un simple glissement laissait les logos sauter
                // d'une place a l'autre en pleine course.
                int alpha = (int)(255f * t * (0.25f + 0.75f * p));
                if (alpha <= 0) return;

                float rayon = Rayon * (0.9f + 0.1f * t) * (0.94f + 0.06f * p);
                float cx = Ecran.CentreX + (1f - p) * 34f * -_sensPage;
                float cy = CentreY;

                Roue(c, m, cx, cy, rayon, alpha, total);
                Coeur(c, m, cx, cy, alpha);
            }
            catch (Exception ex) { Journal.Erreur("dessin de la roue", ex); }
        }

        // ------------------------------------------------------------------
        //  Les secteurs
        // ------------------------------------------------------------------
        private void Roue(Config c, Menu m, float cx, float cy, float rayon,
                          int alpha, int total)
        {
            Pays pays = c.Pays[m.PaysCourant];

            // Un pays sans station laissait l'ecran vide, sans rien dire : le
            // joueur croyait la roue cassee plutot que le fichier incomplet.
            if (pays.Stations.Count == 0)
            {
                Dessin.Texte("AUCUNE STATION", cx, cy - rayon - 8f, 0.38f,
                             Color.FromArgb(alpha * 190 / 255, 235, 180, 172),
                             Alignment.Center);
                return;
            }

            for (int s = 0; s < total; s++)
            {
                float angle = Menu.AngleSecteur(s, total);
                float x = cx + (float)Math.Sin(angle) * rayon;
                float y = cy - (float)Math.Cos(angle) * rayon;

                // Proximite continue : 1 pile sur le secteur, 0 des le suivant.
                // Tout le rendu en derive, donc rien ne change par paliers.
                float prox = 1f - Distance(s, total);
                if (prox < 0f) prox = 0f;
                prox = prox * prox * (3f - 2f * prox);        // courbe en S

                int a = (int)(alpha * (0.42f + 0.58f * prox));
                float echelle = TexteAutre + (TexteVise - TexteAutre) * prox;

                if (s == 0) { Eteindre(x, y, echelle, a); continue; }

                Station st = pays.Stations[s - 1];
                float boite = LogoAutre + (LogoVise - LogoAutre) * prox;

                // Contenu dans sa boite sans deformation : les logos ne sont
                // pas carres, les forcer en carre les rendait meconnaissables.
                float l, h;
                Dessin.Contenir(st.Icone, boite, out l, out h);
                Dessin.ImageCentree(st.Icone, x, y - 4f, boite, a);

                Dessin.Texte(st.Nom.ToUpperInvariant(), x, y - 4f + h / 2f + 2f,
                             echelle, Teinte(prox, a), Alignment.Center);
            }
        }

        /// <summary>
        /// Le secteur d'extinction, toujours a midi. Pas de logo : un cercle
        /// barre serait a dessiner, alors que le mot se lit d'un coup.
        /// </summary>
        private static void Eteindre(float x, float y, float echelle, int alpha)
        {
            Dessin.Texte("RADIO OFF", x, y - 10f, echelle + 0.04f,
                         Color.FromArgb(alpha, 255, 255, 255), Alignment.Center);
        }

        // ------------------------------------------------------------------
        //  Le coeur : le pays, et par ou changer de page
        // ------------------------------------------------------------------
        private static void Coeur(Config c, Menu m, float cx, float cy, int alpha)
        {
            Pays pays = c.Pays[m.PaysCourant];

            if (!string.IsNullOrEmpty(pays.Drapeau))
            {
                const float td = 40f;
                float l, h;
                Dessin.Contenir(pays.Drapeau, td, out l, out h);
                Dessin.ImageCentree(pays.Drapeau, cx, cy - 16f - h / 2f, td, alpha);
            }

            Dessin.Texte(pays.Nom.ToUpperInvariant(), cx, cy - 8f, 0.40f,
                         Color.FromArgb(alpha, 255, 255, 255), Alignment.Center);

            // Un seul chevron, a droite : le pays ne se change que dans un sens
            // et on fait le tour. Deux chevrons auraient annonce un bouton qui
            // n'existe pas.
            //
            // En ASCII, comme tout le texte dessine : la police du HUD de GTA
            // n'a pas les caracteres typographiques, et remplace ceux qui lui
            // manquent par un rectangle vide.
            if (c.Pays.Count > 1)
            {
                Dessin.Texte(">", cx + 54f, cy - 11f, 0.42f,
                             Color.FromArgb(alpha * 115 / 255, 215, 215, 215),
                             Alignment.Center);
            }

            Pages(cx, cy + 22f, c.Pays.Count, m.PaysCourant, alpha);
        }

        /// <summary>
        /// Une rangee de points : combien de pays, et lequel est ouvert. Des
        /// points plutot qu'un chiffre sur un total : la meme information, sans
        /// ajouter du texte a un ecran qui doit rester sobre.
        /// </summary>
        private static void Pages(float cx, float y, int total, int actif, int alpha)
        {
            if (total < 2 || alpha <= 0) return;

            const float taille = 3.5f;
            const float pas = 11f;
            float depart = cx - (total - 1) * pas / 2f - taille / 2f;

            for (int i = 0; i < total; i++)
            {
                bool ici = i == actif;
                float t = ici ? taille + 1.5f : taille;
                Dessin.VoileClair(depart + i * pas, y + (ici ? -0.75f : 0f), t, t,
                                  alpha * (ici ? 230 : 75) / 255);
            }
        }

        /// <summary>Du gris clair au blanc franc, selon la proximite.</summary>
        private static Color Teinte(float prox, int a)
        {
            int g = 212 + (int)(43f * prox);
            if (g > 255) g = 255;
            return Color.FromArgb(a, g, g, g);
        }
    }
}
