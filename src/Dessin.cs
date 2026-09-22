// ============================================================================
//  Dessin  -  primitives communes et cache d'images
// ----------------------------------------------------------------------------
//  Les sprites sont caches par CHEMIN, jamais par nom de station ni de pays :
//  indexer sur un identifiant qui ne change pas fige la premiere image chargee,
//  ce qui etait le defaut de l'ancien selecteur.
//
//  Les pochettes changent a chaque morceau : elles ont leur propre emplacement
//  unique plutot que d'alimenter un cache qui grossirait sans fin.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using GTA.UI;

namespace WorldRadio
{
    internal static class Dessin
    {
        private static readonly Dictionary<string, CustomSprite> _cache =
            new Dictionary<string, CustomSprite>(StringComparer.OrdinalIgnoreCase);

        private static string _dossier = "";
        private static string _cheminPochette;
        private static CustomSprite _spritePochette;

        internal static void Init(Config c)
        {
            _dossier = c.DossierIcones;
            _cache.Clear();
            Proportions.Init(c.DossierIcones);
            _cheminPochette = null;
            _spritePochette = null;
        }

        // ------------------------------------------------------------------
        //  Texte
        // ------------------------------------------------------------------
        // OMBRE PORTEE, jamais de contour. Le contour epaissit chaque lettre
        // et donne un aspect grossier ; l ombre est ce qu emploie le HUD de
        // GTA, et elle suffit largement a detacher du texte clair pose sur un
        // ciel clair.
        internal static void Texte(string texte, float x, float y, float echelle,
                                   Color couleur, Alignment alignement)
        {
            if (string.IsNullOrEmpty(texte)) return;
            new TextElement(texte, new PointF(x, y), echelle, couleur,
                            GTA.UI.Font.ChaletComprimeCologne,
                            alignement, true, false).ScaledDraw();
        }

        /// <summary>Texte pose sans aucun fond : meme rendu, l ombre suffit.</summary>
        internal static void TexteDetache(string texte, float x, float y, float echelle,
                                          Color couleur, Alignment alignement)
        {
            Texte(texte, x, y, echelle, couleur, alignement);
        }

        /// <summary>Bande claire, pour une jauge posee sur un voile sombre.</summary>
        internal static void VoileClair(float x, float y, float l, float h, int alpha)
        {
            if (alpha <= 0 || l <= 0f) return;
            new ContainerElement(new PointF(x, y), new SizeF(l, h),
                                 Color.FromArgb(alpha, 235, 240, 250)).ScaledDraw();
        }

        internal static void Voile(float x, float y, float l, float h, int alpha)
        {
            if (alpha <= 0) return;
            new ContainerElement(new PointF(x, y), new SizeF(l, h),
                                 Color.FromArgb(alpha, 0, 0, 0)).ScaledDraw();
        }

        // ------------------------------------------------------------------
        //  Troncature
        // ------------------------------------------------------------------
        //  Mesuree, pas comptee en caracteres. Compter les caracteres suppose
        //  une largeur moyenne, ce qui est faux deux fois : la police est
        //  proportionnelle (un « W » vaut quatre « i »), et la limite doit
        //  dependre de l'echelle. L'ancienne version appliquait la MEME limite
        //  au titre en 0,36 et a l'artiste en 0,28 : le second debordait du
        //  cadre alors qu'il restait de la place au premier.

        private static readonly Dictionary<string, string> _coupes =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Largeur du texte dans l'espace de dessin, ou -1 si la mesure echoue.</summary>
        private static float LargeurTexte(string texte, float echelle)
        {
            try
            {
                return TextElement.GetScaledStringWidth(texte, GTA.UI.Font.ChaletComprimeCologne,
                                                        echelle);
            }
            catch { return -1f; }
        }

        /// <summary>
        /// Tronque pour tenir dans une largeur donnee, sans couper au milieu
        /// d'un mot quand c'est possible. Le resultat est retenu : la mesure
        /// passe par une native, inutile de la refaire soixante fois par
        /// seconde pour un titre qui ne change pas.
        /// </summary>
        internal static string Couper(string texte, float largeurMax, float echelle)
        {
            if (string.IsNullOrEmpty(texte) || largeurMax <= 0f) return texte;

            string cle = echelle.ToString("0.000") + "|"
                       + ((int)largeurMax).ToString() + "|" + texte;
            string resultat;
            if (_coupes.TryGetValue(cle, out resultat)) return resultat;

            resultat = Tailler(texte, largeurMax, echelle);

            // Les titres defilent au fil des morceaux : on vide plutot que de
            // laisser le dictionnaire grossir toute une session.
            if (_coupes.Count > 200) _coupes.Clear();
            _coupes[cle] = resultat;
            return resultat;
        }

        private static string Tailler(string texte, float largeurMax, float echelle)
        {
            float entiere = LargeurTexte(texte, echelle);

            // Mesure indisponible : on retombe sur une estimation au caractere,
            // large de la police condensee employee par le HUD.
            if (entiere < 0f)
            {
                int maximum = (int)(largeurMax / (12f * echelle));
                if (maximum < 4 || texte.Length <= maximum) return texte;
                return AuMot(texte, maximum);
            }

            if (entiere <= largeurMax) return texte;

            // recherche du plus long prefixe qui tienne, ellipse comprise
            int bas = 0, haut = texte.Length;
            while (bas < haut)
            {
                int milieu = (bas + haut + 1) / 2;
                if (LargeurTexte(texte.Substring(0, milieu) + "...", echelle) <= largeurMax)
                    bas = milieu;
                else
                    haut = milieu - 1;
            }
            if (bas <= 0) return "...";
            return AuMot(texte, bas);
        }

        /// <summary>
        /// Recule jusqu'a la fin du mot precedent, si elle n'est pas trop loin.
        /// Les trois points sont ecrits en ASCII : la police du HUD n'a pas le
        /// caractere de suspension et le remplace par un rectangle vide.
        /// </summary>
        private static string AuMot(string texte, int coupure)
        {
            if (coupure >= texte.Length) return texte;
            string coupe = texte.Substring(0, coupure);
            int espace = coupe.LastIndexOf(' ');
            if (espace > coupure / 2) coupe = coupe.Substring(0, espace);
            return coupe.TrimEnd() + "...";
        }

        // ------------------------------------------------------------------
        //  Images
        // ------------------------------------------------------------------
        // ------------------------------------------------------------------
        //  Images
        // ------------------------------------------------------------------
        //  Les proportions reelles sont lues par WorldRadio.Proportions, a
        //  l'ecart : Dessin reference des types du jeu et ne peut donc pas
        //  etre instancie hors de GTA, ce qui rendrait la regle invisible aux
        //  tests. Ici on ne fait que dessiner.

        /// <summary>Dimensions d'une image contenue dans une boite, sans deformation.</summary>
        internal static void Contenir(string fichier, float boite, out float l, out float h)
        {
            Proportions.Contenir(fichier, boite, out l, out h);
        }

        /// <summary>Image contenue dans une boite, calee en haut a gauche.</summary>
        internal static void Image(string fichier, float x, float y, float boite, int alpha)
        {
            if (string.IsNullOrEmpty(fichier)) return;
            float l, h;
            Contenir(fichier, boite, out l, out h);
            ImageEtiree(fichier, x + (boite - l) / 2f, y + (boite - h) / 2f, l, h, alpha);
        }

        /// <summary>Image contenue dans une boite, centree sur un point.</summary>
        internal static void ImageCentree(string fichier, float cx, float cy, float boite, int alpha)
        {
            if (string.IsNullOrEmpty(fichier)) return;
            float l, h;
            Contenir(fichier, boite, out l, out h);
            ImageEtiree(fichier, cx - l / 2f, cy - h / 2f, l, h, alpha);
        }

        /// <summary>
        /// Image etiree a un rectangle quelconque, pour le degrade de fond.
        /// </summary>
        internal static void ImageEtiree(string fichier, float x, float y,
                                         float l, float h, int alpha)
        {
            if (string.IsNullOrEmpty(fichier) || alpha <= 0 || l <= 0f || h <= 0f) return;

            CustomSprite s;
            if (!_cache.TryGetValue(fichier, out s))
            {
                s = Charger(Path.Combine(_dossier, fichier));
                _cache[fichier] = s;              // meme null : on ne reessaie pas
            }
            if (s == null) return;

            s.Position = new PointF(x, y);
            s.Size = new SizeF(l, h);
            s.Color = Color.FromArgb(alpha, 255, 255, 255);
            s.ScaledDraw();
        }

        /// <summary>Image designee par un chemin absolu : pochette telechargee.</summary>
        internal static void Pochette(string chemin, float x, float y, float taille, int alpha)
        {
            if (string.IsNullOrEmpty(chemin) || alpha <= 0) return;

            if (chemin != _cheminPochette)
            {
                _cheminPochette = chemin;
                _spritePochette = Charger(chemin);
            }
            Poser(_spritePochette, x, y, taille, alpha);
        }

        private static CustomSprite Charger(string chemin)
        {
            if (!File.Exists(chemin))
            {
                Journal.Ecrire("image introuvable : " + chemin);
                return null;
            }
            try
            {
                return new CustomSprite(chemin, new SizeF(64f, 64f), new PointF(0f, 0f),
                                        Color.FromArgb(255, 255, 255, 255));
            }
            catch (Exception ex) { Journal.Erreur("chargement de " + chemin, ex); return null; }
        }

        // Position, taille et teinte sont reappliquees a chaque dessin : le
        // meme sprite sert a plusieurs endroits, a des tailles differentes.
        private static void Poser(CustomSprite s, float x, float y, float taille, int alpha)
        {
            if (s == null) return;
            s.Position = new PointF(x, y);
            s.Size = new SizeF(taille, taille);
            s.Color = Color.FromArgb(alpha, 255, 255, 255);
            s.ScaledDraw();
        }

        // ------------------------------------------------------------------
        //  Animation
        // ------------------------------------------------------------------
        /// <summary>Progression de 0 a 1 sur une duree, adoucie aux extremites.</summary>
        internal static float Progression(int debut, int duree)
        {
            if (debut == int.MinValue) return 1f;
            int ecoule = Environment.TickCount - debut;
            if (ecoule < 0 || ecoule >= duree) return 1f;
            float t = (float)ecoule / duree;
            return t * t * (3f - 2f * t);         // courbe en S, sans a-coup
        }
    }
}
