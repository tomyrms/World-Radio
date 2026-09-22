// ============================================================================
//  Pause  -  detecter que le jeu est fige, sans dependre des natives
// ----------------------------------------------------------------------------
//  Pourquoi ne pas simplement demander au jeu.
//
//  Sur cette installation Enhanced, les natives prevues pour cela ne repondent
//  PAS. Le journal d'une session complete le montre : la ligne d'etat de pause
//  n'a jamais change, alors que le joueur a ouvert le menu cinq fois.
//
//      14:07:02  verrou arme sur appui touche
//      14:07:02  menu referme, relecture du volume     <- la meme seconde
//
//  Et Game.IsPaused n'aide pas davantage : son desassemblage montre qu'il
//  appelle exactement la meme native, IS_PAUSE_MENU_ACTIVE. Les deux sources
//  n'en font donc qu'une, et elle est muette.
//
//  Ce qui marche, en revanche : l'HORLOGE DU JEU s'arrete quand le jeu est
//  fige, alors que l'horloge du systeme continue. Comparer les deux donne une
//  detection qui ne depend d'aucune native, et qui couvre au passage tout ce
//  qui fige le jeu, menu pause compris.
//
//  Un seuil evite de confondre une pause avec un simple a-coup d'affichage.
// ============================================================================

using System;

namespace WorldRadio
{
    internal sealed class DetecteurPause
    {
        /// <summary>Au-dela, le jeu est considere comme fige, pas simplement lent.</summary>
        private const int SeuilFige = 220;     // ms

        private int _horlogeJeu = -1;
        private int _horlogeReelle;
        private int _figeDepuis;
        private bool _fige;
        private bool _vientDeReprendre;

        /// <summary>Vrai quand le jeu ne progresse plus : menu pause, chargement...</summary>
        internal bool Fige { get { return _fige; } }

        /// <summary>Derniere duree de figement observee, pour le journal.</summary>
        internal int FigeDepuis { get { return _figeDepuis; } }

        /// <summary>
        /// Vrai sur la premiere image suivant un gel. Le journal a montre que
        /// le script ne tourne PAS pendant le menu pause : un grand trou entre
        /// deux ticks signifie donc qu on vient d en sortir, et le son peut
        /// revenir aussitot sans attendre que le joueur bouge.
        /// </summary>
        internal bool VientDeReprendre { get { return _vientDeReprendre; } }

        internal void Rafraichir()
        {
            int jeu = Natif.HorlogeJeu();
            int reelle = Environment.TickCount;

            if (_horlogeJeu < 0)          // premiere image
            {
                _horlogeJeu = jeu;
                _horlogeReelle = reelle;
                return;
            }

            int ecoulReel = reelle - _horlogeReelle;
            if (ecoulReel < 0) ecoulReel = 0;        // TickCount a boucle
            _horlogeReelle = reelle;

            // un trou de plus d une demi-seconde entre deux images : le script
            // n a pas tourne, donc le jeu etait fige
            _vientDeReprendre = ecoulReel > 500;

            if (jeu != _horlogeJeu)
            {
                // le jeu avance : tout va bien
                _horlogeJeu = jeu;
                _figeDepuis = 0;
                _fige = false;
                return;
            }

            // l'horloge du jeu n'a pas bouge alors que le temps reel passe
            _figeDepuis += ecoulReel;
            if (_figeDepuis >= SeuilFige) _fige = true;
        }

        /// <summary>Force l'etat fige : appui sur la touche pause, avant meme le menu.</summary>
        internal void Forcer()
        {
            _fige = true;
            _figeDepuis = SeuilFige;
        }
    }
}
