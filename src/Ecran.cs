// ============================================================================
//  Ecran  -  geometrie relative, et zone de securite
// ----------------------------------------------------------------------------
//  ScaledDraw travaille dans un espace de hauteur fixe 720, dont la LARGEUR
//  depend du ratio de l'ecran : 1280 en 16:9, 1152 en 16:10, environ 1680 en
//  21:9 et 2560 en 32:9. Coder 1280 en dur collerait donc l'interface au
//  mauvais endroit sur tout ecran non 16:9.
//
//  La zone de securite est le reglage GTA qui eloigne le HUD des bords, pour
//  les televiseurs qui rognent l'image. On la respecte pour que le composant
//  en bas a droite ne soit jamais colle au bord.
// ============================================================================

using System;
using GTA.Native;

namespace WorldRadio
{
    internal static class Ecran
    {
        internal const float Hauteur = 720f;

        /// <summary>Largeur de l'espace de dessin, variable selon le ratio.</summary>
        internal static float Largeur
        {
            get
            {
                try
                {
                    float l = GTA.UI.Screen.ScaledWidth;
                    if (l > 100f && l < 8000f) return l;
                }
                catch { }
                return 1280f;               // repli 16:9
            }
        }

        internal static float CentreX { get { return Largeur / 2f; } }
        internal const float CentreY = Hauteur / 2f;

        /// <summary>
        /// Marge imposee par la zone de securite, en unites de dessin.
        /// GET_SAFE_ZONE_SIZE renvoie environ 0,85 a 1,00 ; la fraction perdue
        /// se repartit de part et d'autre.
        /// </summary>
        private static float _marge = -1f;
        private static int _margeLue;

        /// <summary>
        /// Relue une fois par seconde, pas a chaque image : le joueur ne change
        /// pas sa zone de securite soixante fois par seconde, et une native par
        /// image pour une valeur constante est du gaspillage.
        /// </summary>
        internal static float Marge
        {
            get
            {
                int maintenant = Environment.TickCount;
                if (_marge >= 0f && maintenant - _margeLue < 1000) return _marge;
                _margeLue = maintenant;

                float zone = 1f;
                try { zone = Function.Call<float>(Hash.GET_SAFE_ZONE_SIZE); }
                catch { }
                if (zone < 0.5f || zone > 1f) zone = 0.9f;

                // une marge minimale meme zone de securite au maximum : le HUD
                // ne doit jamais toucher le bord
                _marge = Math.Max(16f, (1f - zone) * Hauteur * 0.5f + 16f);
                return _marge;
            }
        }
    }
}
