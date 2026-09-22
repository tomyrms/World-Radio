// ============================================================================
//  Spectre  -  niveau sonore reel, par bandes de frequence
// ----------------------------------------------------------------------------
//  Les barres montrent ce qui SORT vraiment du flux. Rien n'est simule : une
//  animation decorative qui bougerait sans rapport avec le son serait plus
//  trompeuse qu'utile, et ne dirait rien quand la station se tait.
//
//  Elles servent donc aussi de temoin : si elles sont plates alors que
//  l'encart annonce "EN DIRECT", c'est que plus aucun echantillon n'arrive.
//
//  Deux moities, sur deux fils :
//
//      SondeSpectre  s'insere dans la chaine audio et recopie les
//                    echantillons dans un tampon circulaire. Elle tourne sur
//                    le fil audio, ou tout retard s'entend : elle ne fait donc
//                    qu'une recopie, aucun calcul.
//
//      Spectre       lit ce tampon depuis le fil du jeu et en tire cinq
//                    niveaux par transformee de Fourier. Vingt fois par
//                    seconde au plus : l'oeil n'en demande pas davantage, et
//                    une FFT par image serait du gaspillage.
//
//  La sonde est placee AVANT l'etage de gain : les barres suivent la musique
//  diffusee, pas le volume auquel on l'ecoute. Baisser le son ne doit pas
//  ecraser l'affichage.
// ============================================================================

using System;
using NAudio.Dsp;
using NAudio.Wave;

namespace WorldRadio
{
    /// <summary>
    /// Maillon transparent de la chaine audio : il laisse passer les
    /// echantillons sans les modifier, et en garde une copie.
    /// </summary>
    internal sealed class SondeSpectre : ISampleProvider
    {
        internal const int Taille = 1024;          // puissance de deux, ~23 ms
        private const int Masque = Taille - 1;

        private readonly ISampleProvider _source;
        private readonly float[] _anneau = new float[Taille];
        private readonly object _verrou = new object();
        private int _ecriture;

        internal SondeSpectre(ISampleProvider source) { _source = source; }

        public WaveFormat WaveFormat { get { return _source.WaveFormat; } }

        public int Read(float[] buffer, int offset, int count)
        {
            int lus = _source.Read(buffer, offset, count);
            if (lus <= 0) return lus;

            int canaux = _source.WaveFormat.Channels;
            if (canaux < 1) canaux = 1;

            lock (_verrou)
            {
                // les canaux sont replies en mono : un spectre par oreille
                // n'apporterait rien a cinq barres larges comme un doigt
                for (int i = 0; i + canaux <= lus; i += canaux)
                {
                    float somme = 0f;
                    for (int c = 0; c < canaux; c++) somme += buffer[offset + i + c];
                    _anneau[_ecriture] = somme / canaux;
                    _ecriture = (_ecriture + 1) & Masque;
                }
            }
            return lus;
        }

        /// <summary>Recopie le tampon dans l'ordre chronologique.</summary>
        internal void Copier(float[] destination)
        {
            lock (_verrou)
            {
                int debut = _ecriture;
                for (int i = 0; i < Taille; i++)
                    destination[i] = _anneau[(debut + i) & Masque];
            }
        }
    }

    // ------------------------------------------------------------------

    internal sealed class Spectre
    {
        internal const int Bandes = 5;

        private const int Exposant = 10;           // 2^10 = SondeSpectre.Taille
        private const int PeriodeMs = 50;          // vingt calculs par seconde

        // Bornes des bandes, en hertz. Espacees logarithmiquement : l'oreille
        // percoit les frequences ainsi, et des bandes lineaires donneraient
        // quatre barres muettes et une seule qui bouge.
        private static readonly float[] Bornes = { 40f, 160f, 400f, 1000f, 2500f, 8000f };

        // Montee vive, descente lente : une barre qui retombe aussi vite
        // qu'elle monte scintille et devient illisible.
        private const float Montee = 0.55f;
        private const float Descente = 0.13f;

        private readonly float[] _tampon = new float[SondeSpectre.Taille];
        private readonly Complex[] _fft = new Complex[SondeSpectre.Taille];
        private readonly float[] _niveaux = new float[Bandes];
        private int _dernierCalcul = int.MinValue;

        /// <summary>Niveau d'une bande, de 0 a 1.</summary>
        internal float Niveau(int bande)
        {
            if (bande < 0 || bande >= Bandes) return 0f;
            return _niveaux[bande];
        }

        /// <summary>Tout est a zero : rien n'arrive, ou tout est silencieux.</summary>
        internal bool Muet
        {
            get
            {
                for (int i = 0; i < Bandes; i++) if (_niveaux[i] > 0.02f) return false;
                return true;
            }
        }

        internal void Reinitialiser()
        {
            for (int i = 0; i < Bandes; i++) _niveaux[i] = 0f;
            _dernierCalcul = int.MinValue;
        }

        // ------------------------------------------------------------------

        internal void Rafraichir(SondeSpectre sonde, int frequence, bool audible)
        {
            // Coupe ou a l'arret : les barres retombent au lieu de se figer sur
            // leur derniere valeur, qui laisserait croire a un son present.
            if (sonde == null || !audible) { Retomber(); return; }

            int maintenant = Environment.TickCount;
            if (_dernierCalcul != int.MinValue && maintenant - _dernierCalcul < PeriodeMs)
                return;
            _dernierCalcul = maintenant;

            try { Calculer(sonde, frequence); }
            catch (Exception ex) { Journal.Erreur("calcul du spectre", ex); Retomber(); }
        }

        private void Retomber()
        {
            for (int i = 0; i < Bandes; i++) _niveaux[i] *= (1f - Descente);
        }

        private void Calculer(SondeSpectre sonde, int frequence)
        {
            if (frequence <= 0) frequence = 44100;
            sonde.Copier(_tampon);

            // Fenetre de Hann : sans elle, les bords nets du tampon ajoutent au
            // spectre des frequences qui ne sont pas dans le son.
            for (int i = 0; i < SondeSpectre.Taille; i++)
            {
                _fft[i].X = _tampon[i] * (float)FastFourierTransform.HannWindow(i, SondeSpectre.Taille);
                _fft[i].Y = 0f;
            }

            FastFourierTransform.FFT(true, Exposant, _fft);

            float parCase = (float)frequence / SondeSpectre.Taille;
            int moitie = SondeSpectre.Taille / 2;

            for (int b = 0; b < Bandes; b++)
            {
                int premiere = (int)(Bornes[b] / parCase);
                int derniere = (int)(Bornes[b + 1] / parCase);
                if (premiere < 1) premiere = 1;
                if (derniere >= moitie) derniere = moitie - 1;
                if (derniere < premiere) derniere = premiere;

                float sommet = 0f;
                for (int i = premiere; i <= derniere; i++)
                {
                    float m = _fft[i].X * _fft[i].X + _fft[i].Y * _fft[i].Y;
                    if (m > sommet) sommet = m;
                }

                _niveaux[b] = Lisser(_niveaux[b], EnHauteur((float)Math.Sqrt(sommet)));
            }
        }

        // Plage utile, en decibels. Mesuree sur un flux reel plutot que
        // choisie au jugement : Skyrock tenait entre -37 et -26 dB, ce qui sur
        // une echelle -62..0 dB collait toutes les barres a mi-hauteur sans
        // qu'elles bougent. La radio est compressee, sa dynamique est etroite :
        // on cadre dessus au lieu d'etaler sur une plage que rien n'occupe.
        private const float PlancherDb = -52f;
        private const float PlafondDb = -16f;

        /// <summary>
        /// Amplitude convertie en hauteur visible. L'echelle est en decibels :
        /// en lineaire, tout le mouvement se tasse en bas de la barre et seul
        /// un coup de grosse caisse se voit.
        /// </summary>
        private static float EnHauteur(float amplitude)
        {
            if (amplitude <= 1e-7f) return 0f;
            float db = 20f * (float)Math.Log10(amplitude);
            float h = (db - PlancherDb) / (PlafondDb - PlancherDb);
            if (h < 0f) return 0f;
            if (h > 1f) return 1f;
            return h;
        }

        private static float Lisser(float actuel, float vise)
        {
            float k = vise > actuel ? Montee : Descente;
            return actuel + (vise - actuel) * k;
        }
    }
}
