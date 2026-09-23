// ============================================================================
//  Lecteur  -  flux internet, volume, coupure, reconnexion
// ----------------------------------------------------------------------------
//  REGLE ABSOLUE : ne jamais toucher IWavePlayer.Volume.
//
//  Dans NAudio, cette propriete ne regle PAS le volume du flux :
//      WasapiOut.Volume     -> AudioEndpointVolume.MasterVolumeLevelScalar,
//                              soit le volume du peripherique Windows entier
//      WaveOutEvent.Volume  -> WaveOutUtils.SetWaveOutVolume, soit toute la
//                              session audio du processus
//  L'ecrire baisse le son du jeu et de tout le systeme, et une coupure a 0
//  laisse l'utilisateur sans aucun son jusqu'a ce qu'il le remonte a la main.
//
//  Le volume passe donc exclusivement par VolumeWaveProvider16, insere dans
//  la chaine audio : il n'agit que sur nos propres echantillons.
//  Un test de compilation verifie cette regle sur la DLL produite.
//
//  Sortie : WasapiOut en mode PARTAGE, fait pour que plusieurs flux
//  coexistent, plutot que WaveOut qui passe par la vieille couche winmm.
// ============================================================================

using System;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace WorldRadio
{
    internal sealed class Lecteur
    {
        private readonly object _verrou = new object();

        // Numero de la demande en cours. Toute connexion portant un numero
        // perime est abandonnee avant d avoir emis le moindre son.
        private int _generation;

        private IWavePlayer _sortie;
        private MediaFoundationReader _source;
        private VolumeSampleProvider _attenuateur;   // etage de gain, dans la chaine
        private SondeSpectre _sonde;                 // derivation, avant le gain
        private volatile int _frequence;

        /// <summary>Derivation d'ou l'affichage tire les niveaux, ou null.</summary>
        internal SondeSpectre Sonde { get { lock (_verrou) { return _sonde; } } }

        /// <summary>Frequence d'echantillonnage du flux en cours.</summary>
        internal int Frequence { get { return _frequence; } }

        private volatile string _urlVoulue;

        // Gain applique a NOTRE flux. Le lecteur ne le DECIDE jamais : il le
        // recoit du ControleurAudio, seule autorite du volume.
        private float _gain;
        private bool _mfPret;
        private DateTime _prochaineTentative = DateTime.MinValue;

        /// <summary>Station en cours, ou null si rien ne joue.</summary>
        internal string UrlEnCours { get { return _urlVoulue; } }

        private volatile bool _enConnexion;
        private volatile bool _injoignable;

        /// <summary>
        /// La derniere tentative a echoue. L encart le dit plutot que de
        /// laisser un silence qu on prendrait pour une panne du mod.
        /// </summary>
        internal bool Injoignable { get { return _injoignable; } }

        /// <summary>Une connexion est en cours : l encart le dit plutot que rester muet.</summary>
        internal bool EnConnexion { get { return _enConnexion; } }

        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        //  Un seul ouvrier, et une seule demande en attente
        // ------------------------------------------------------------------
        //  La version precedente creait un thread par changement de station,
        //  tous en file sur un meme verrou. Une station lente a repondre les
        //  laissait s'empiler : dix secondes de connexion et un survol nerveux
        //  suffisaient a en accumuler une vingtaine, chacun avec sa pile.
        //
        //  Un ouvrier unique serialise par construction, sans verrou de
        //  connexion. Et il ne traite que la DERNIERE demande connue : les
        //  intermediaires sont sautees plutot qu'executees puis jetees.
        private Thread _ouvrier;
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private volatile bool _arrete;

        internal void Jouer(string url)
        {
            lock (_verrou)
            {
                _generation++;
                _urlVoulue = url;
            }
            // Ouvrir un flux reseau bloque de une a plusieurs secondes : jamais
            // sur le thread du jeu, l'image s'y figerait.
            _prochaineTentative = DateTime.UtcNow.AddSeconds(10);
            Reveiller();
        }

        internal void Arreter()
        {
            lock (_verrou)
            {
                _generation++;                 // perime toute connexion en cours
                _urlVoulue = null;
            }
            Reveiller();
        }

        private void Reveiller()
        {
            lock (_verrou)
            {
                if (_ouvrier == null)
                {
                    _ouvrier = new Thread((ThreadStart)Travailler);
                    _ouvrier.IsBackground = true;
                    _ouvrier.Name = "WorldRadio-Connexion";
                    _ouvrier.Start();
                }
            }
            _signal.Set();
        }

        /// <summary>
        /// Arret definitif : rend la sortie audio et met fin a l'ouvrier.
        ///
        /// SHVDN recharge les scripts a la touche Inser. Un thread laisse
        /// vivant survivrait au rechargement en referencant l'ANCIENNE
        /// assembly : deux lecteurs coexisteraient, et le plus ancien
        /// n'obeirait plus a personne.
        /// </summary>
        internal void Eteindre()
        {
            lock (_verrou) { _generation++; _urlVoulue = null; }
            _arrete = true;
            _signal.Set();

            Thread t;
            lock (_verrou) { t = _ouvrier; }

            // Une connexion en cours peut bloquer plusieurs secondes sur le
            // reseau : on ne fige pas le jeu a l'attendre. Le numero de
            // generation a deja ete avance, la connexion tardive se liberera
            // donc d'elle-meme sans jamais emettre un son.
            if (t != null) { try { t.Join(400); } catch { } }
            Fermer();
        }

        private void Travailler()
        {
            while (true)
            {
                _signal.WaitOne();
                if (_arrete) { try { Fermer(); } catch { } return; }

                // On relit l'etat au dernier moment : pendant une connexion
                // lente, le joueur a pu changer d'avis plusieurs fois. Seule
                // la derniere demande compte.
                string url;
                int gen;
                lock (_verrou) { url = _urlVoulue; gen = _generation; }

                try
                {
                    if (url == null) Fermer();
                    else Ouvrir(url, gen);
                }
                catch (Exception ex) { Journal.Erreur("ouvrier de connexion", ex); }
            }
        }

        /// <summary>
        /// Une demande plus recente est-elle arrivee depuis. Survoler les
        /// stations en lance une par station : sans ce controle, celle qui
        /// perd la course jouerait quand meme, sans que personne ne detienne
        /// sa reference, donc hors de portee du reglage de gain.
        /// </summary>
        private bool Perimee(int gen)
        {
            lock (_verrou) { return gen != _generation; }
        }

        /// <summary>
        /// Applique un gain deja calcule, entre 0 et 1. Seul point d'entree du
        /// volume.
        ///
        /// Une coupure se fait a gain NUL, jamais par une mise en pause de la
        /// sortie : sur un direct, une pause prendrait du retard sur
        /// l'emission et la reprise serait decalee de la duree de la pause.
        /// A gain nul le flux continue d'etre consomme, donc de rester
        /// synchrone avec la diffusion reelle.
        /// </summary>
        internal void DefinirGain(float g)
        {
            if (g < 0f) g = 0f; else if (g > 1f) g = 1f;
            _gain = g;
            lock (_verrou)
            {
                if (_attenuateur == null) return;
                try { _attenuateur.Volume = g; }
                catch (Exception ex) { Journal.Erreur("application du gain", ex); }
            }
        }

        // ------------------------------------------------------------------

        // Appelee UNIQUEMENT depuis l'ouvrier : deux ouvertures ne peuvent
        // donc pas se chevaucher, sans avoir a s'en remettre a un verrou.
        private void Ouvrir(string url, int gen)
        {
            try
            {
                if (Perimee(gen)) return;
                _enConnexion = true;
                _injoignable = false;

                DemarrerMediaFoundation();
                Fermer();                       // l'ancienne meurt avant la nouvelle
                if (Perimee(gen)) return;

                // Chaine audio, le gain AVANT le peripherique de sortie :
                //
                //   HTTP -> MediaFoundationReader -> ISampleProvider
                //        -> SondeSpectre -> VolumeSampleProvider
                //        -> Limiteur     -> WasapiOut
                //
                // Le gain multiplie donc reellement les echantillons. Ecrire
                // dans IWavePlayer.Volume agirait au contraire sur le
                // peripherique Windows entier, ce qui n'est pas un gain de
                // flux mais un reglage systeme.
                MediaFoundationReader source = new MediaFoundationReader(url);
                ISampleProvider echantillons = source.ToSampleProvider();

                // La sonde se place AVANT le gain : les barres suivent la
                // musique diffusee, pas le volume auquel on l'ecoute.
                SondeSpectre sonde = new SondeSpectre(echantillons);

                VolumeSampleProvider attenuateur = new VolumeSampleProvider(sonde);
                attenuateur.Volume = _gain;

                // Le limiteur vient APRES le gain : c'est le signal final qu'il
                // doit surveiller, celui qui sortira reellement.
                ISampleProvider garde = new Limiteur(attenuateur);

                IWavePlayer sortie = CreerSortie();
                sortie.Init(garde);

                // On ne lance le son QU'APRES s'etre assure d'etre encore la
                // demande courante, et une fois la reference enregistree. Un
                // Play() avant ce point produisait une sortie que plus rien ne
                // pilotait : elle continuait de jouer, sourde au volume comme
                // a la pause.
                lock (_verrou)
                {
                    if (gen != _generation)
                    {
                        Liberer(sortie, source);
                        return;
                    }
                    _sortie = sortie;
                    _source = source;
                    _attenuateur = attenuateur;
                    _sonde = sonde;
                    attenuateur.Volume = _gain;     // le gain a pu changer depuis
                }
                _frequence = source.WaveFormat.SampleRate;

                sortie.Play();

                Journal.Ecrire("[audio] decodeur = MediaFoundationReader   "
                               + "gain = VolumeSampleProvider   sortie = WasapiOut partage");
                Journal.Ecrire("[radio] flux connecte : " + url
                               + "   " + source.WaveFormat
                               + "   gain = " + _gain.ToString("0.00"));
            }
            catch (Exception ex)
            {
                Journal.Erreur("ouverture de " + url, ex);
                if (!Perimee(gen)) _injoignable = true;
            }
            finally { _enConnexion = false; }
        }

        private void DemarrerMediaFoundation()
        {
            if (_mfPret) return;
            try
            {
                MediaFoundationApi.Startup();
                _mfPret = true;
                Journal.Ecrire("Media Foundation demarre");
            }
            catch (Exception ex) { Journal.Erreur("demarrage de Media Foundation", ex); }
        }

        private static IWavePlayer CreerSortie()
        {
            // 200 ms de latence : imperceptible pour de la radio, et assez
            // large pour absorber les a-coups reseau.
            IWavePlayer s = new WasapiOut(AudioClientShareMode.Shared, true, 200);
            Journal.Ecrire("sortie audio : WASAPI partage");
            return s;
        }

        private void Fermer()
        {
            IWavePlayer sortie;
            MediaFoundationReader source;
            lock (_verrou)
            {
                sortie = _sortie; source = _source;
                _sortie = null; _source = null; _attenuateur = null; _sonde = null;
            }
            Liberer(sortie, source);
        }

        private static void Liberer(IWavePlayer sortie, MediaFoundationReader source)
        {
            try { if (sortie != null) { sortie.Stop(); sortie.Dispose(); } }
            catch (Exception ex) { Journal.Erreur("fermeture de la sortie", ex); }
            try { if (source != null) source.Dispose(); }
            catch (Exception ex) { Journal.Erreur("fermeture du flux", ex); }
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Relance le flux s'il s'est tu : coupure reseau, fin de tampon, ou
        /// station momentanement injoignable. Appele periodiquement.
        /// </summary>
        internal void Surveiller()
        {
            string url = _urlVoulue;
            if (url == null) return;

            // Une connexion en cours n'a pas encore de sortie : la croire morte
            // la relancerait par-dessus elle-meme. On laisse finir.
            if (_enConnexion) return;
            if (DateTime.UtcNow < _prochaineTentative) return;

            bool mort;
            lock (_verrou)
            {
                mort = _sortie == null || _sortie.PlaybackState == PlaybackState.Stopped;
            }
            if (!mort) return;

            Journal.Ecrire("[radio] flux interrompu, reconnexion");
            Jouer(url);
        }
    }
}
