// ============================================================================
//  Limiteur  -  garde-fou contre la saturation
// ----------------------------------------------------------------------------
//  La correction de niveau par station MONTE certaines radios : HOT 97 est
//  mixee 11 LU sous Skyrock, il faut bien la remonter pour les egaliser. Or ses
//  cretes approchent deja 0 dBFS : sans garde-fou, la remonter ferait saturer.
//
//  Ce maillon ne regle pas le volume, il n'en est pas l'auteur. Il se contente
//  de rabattre ce qui depasserait le seuil. Le reste du temps, il laisse
//  passer le signal a l'identique, echantillon pour echantillon.
//
//  Limiteur a attaque instantanee et relachement doux : l'enveloppe suit la
//  crete au moment meme ou elle arrive, donc aucun echantillon ne franchit le
//  seuil, puis la reduction se relache progressivement. Un simple ecretage
//  couperait net chaque crete, ce qui s'entend comme un craquement ; ici la
//  reduction s'etale sur quelques dizaines de millisecondes.
// ============================================================================

using System;
using NAudio.Wave;

namespace WorldRadio
{
    internal sealed class Limiteur : ISampleProvider
    {
        /// <summary>Seuil, en amplitude lineaire : 0,891 = -1 dBFS.</summary>
        internal const float Seuil = 0.891f;

        private const float RelachementMs = 80f;

        private readonly ISampleProvider _source;
        private readonly float _relachement;   // facteur par trame
        private float _enveloppe;

        internal Limiteur(ISampleProvider source)
        {
            _source = source;
            float fs = source.WaveFormat.SampleRate;
            // constante de temps exprimee par trame : le relachement dure le
            // meme temps quelle que soit la frequence du flux
            _relachement = (float)Math.Exp(-1.0 / (RelachementMs * 0.001 * fs));
        }

        public WaveFormat WaveFormat { get { return _source.WaveFormat; } }

        public int Read(float[] buffer, int offset, int count)
        {
            int lus = _source.Read(buffer, offset, count);
            int canaux = _source.WaveFormat.Channels;
            if (canaux < 1) canaux = 1;

            for (int i = 0; i + canaux <= lus; i += canaux)
            {
                // Une seule enveloppe pour tous les canaux : les reduire
                // separement deplacerait l'image stereo a chaque crete.
                float crete = 0f;
                for (int c = 0; c < canaux; c++)
                {
                    float a = Math.Abs(buffer[offset + i + c]);
                    if (a > crete) crete = a;
                }

                // D'abord laisser decroitre, PUIS prendre le maximum. Comparer
                // la crete a l'enveloppe d'avant sa decroissance laissait passer
                // les cretes situees entre l'ancienne et la nouvelle valeur :
                // mesure faite, un millier d'echantillons par seconde au-dessus
                // du seuil, de 1 - relachement, soit 2,6e-4.
                float decroit = _enveloppe * _relachement;
                _enveloppe = crete > decroit ? crete : decroit;

                if (_enveloppe > Seuil)
                {
                    float g = Seuil / _enveloppe;
                    for (int c = 0; c < canaux; c++) buffer[offset + i + c] *= g;
                }
            }
            return lus;
        }
    }
}
