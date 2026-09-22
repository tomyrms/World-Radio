// ============================================================================
//  Metadonnees  -  artiste et titre en cours de diffusion
// ----------------------------------------------------------------------------
//  Deux sources, choisies par station dans le .ini :
//
//  ICY  -  le titre est intercale dans le flux audio lui-meme. On envoie
//          l'en-tete "Icy-MetaData: 1", le serveur repond un "icy-metaint: N",
//          puis alterne N octets d'audio et un bloc de metadonnees.
//          On n'intercepte pas le flux joue : MediaFoundationReader ouvre sa
//          propre connexion et ne la partage pas. On en ouvre donc une
//          seconde, tres breve. Le cout est faible : il suffit de sauter
//          icy-metaint octets, soit 1 a 16 Ko selon la station.
//
//  TRITON - les stations servies par streamtheworld exposent un point d'acces
//          public "now playing" qui donne en plus l'album et la POCHETTE :
//          https://np.tritondigital.com/public/nowplaying?mountName=...
//
//  Trois formats de titre rencontres :
//      SKYROCK    StreamTitle='Bad Bunny - NUEVAYoL'
//      CIDADE/M80 StreamTitle='<?xml ...><DB_DALET_ARTIST_NAME>...'
//      Z100       StreamTitle='Artiste - text="Titre" song_spot="M" ...'
//      KIIS FM    StreamTitle='title="Titre",artist="ARTISTE",url="...'
//      TRITON     <property name="track_artist_name"><![CDATA[...]]>
//
//  Deux stations n'exposent rien d'exploitable et retombent sur leur nom :
//  NRJ diffuse un identifiant interne, RFM France son propre nom.
// ============================================================================

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace WorldRadio
{
    internal enum SourceMeta { Icy, Triton, Aucune }

    internal sealed class Metadonnees
    {
        private const int PeriodeSecondes = 15;

        private readonly object _verrou = new object();
        private Thread _thread;
        private volatile Station _station;
        private volatile bool _arrete;
        private string _artiste = "";
        private string _titre = "";
        // L URL de pochette reste captee, pour un usage ulterieur, mais elle
        // n est plus transformee en texture : ScriptHookV a fait tomber le jeu
        // sur un fichier servi en PNG alors que son URL annoncait .jpg, et une
        // extension devinee d apres l URL ne dit rien du contenu reel.
        //   FATAL: directx texture ...\cache\1021854669.jpg creation failed
        private string _urlPochette;

        internal string Artiste { get { lock (_verrou) { return _artiste; } } }
        internal string Titre { get { lock (_verrou) { return _titre; } } }
        /// <summary>URL de pochette annoncee par la station, non utilisee pour l instant.</summary>
        internal string UrlPochette { get { lock (_verrou) { return _urlPochette; } } }

        internal void Init(Config c)
        {
        }

        // ------------------------------------------------------------------

        /// <summary>Change de station, ou arrete le suivi si station vaut null.</summary>
        internal void Suivre(Station station)
        {
            lock (_verrou) { _artiste = ""; _titre = ""; _urlPochette = null; }
            _station = station;

            if (station == null) return;
            lock (_verrou)
            {
                if (_thread != null) return;
                _thread = new Thread((ThreadStart)Boucle);
                _thread.IsBackground = true;
                _thread.Name = "WorldRadio-Metadonnees";
                _thread.Start();
            }
        }

        /// <summary>
        /// Met fin au suivi. Sans cela, un rechargement de script (touche
        /// Inser) laisserait ce fil interroger les stations pour le compte
        /// d'un mod qui n'existe plus.
        /// </summary>
        internal void Eteindre()
        {
            _arrete = true;
            _station = null;
        }

        private void Boucle()
        {
            while (!_arrete)
            {
                Station s = _station;
                if (s != null)
                {
                    try
                    {
                        if (s.Source == SourceMeta.Triton) Triton(s);
                        else if (s.Source == SourceMeta.Icy) Icy(s);
                    }
                    catch (WebException) { }          // reseau capricieux, sans consequence
                    catch (Exception ex) { Journal.Erreur("metadonnees de " + s.Nom, ex); }
                }

                // Attente fractionnee plutot qu'un sommeil d'un bloc : changer
                // de station reveille le fil dans la demi-seconde, au lieu de
                // laisser l'encart vide jusqu'a quinze secondes.
                for (int i = 0; i < PeriodeSecondes * 2; i++)
                {
                    Thread.Sleep(500);
                    if (_arrete || _station != s) break;
                }
            }
        }

        /// <summary>Publie le nouveau morceau, sans rien ecraser si la station a change.</summary>
        private void Publier(Station s, string artiste, string titre, string urlPochette)
        {
            if (_station != s) return;

            lock (_verrou)
            {
                if (artiste == _artiste && titre == _titre) return;
                _artiste = artiste;
                _titre = titre;
                _urlPochette = urlPochette;
            }
            Journal.Ecrire("[radio] en cours : " + (artiste.Length > 0 ? artiste + " - " : "") + titre);
        }

        // ==================================================================
        //  Source TRITON
        // ==================================================================
        private void Triton(Station s)
        {
            if (string.IsNullOrEmpty(s.IdMeta)) return;

            string url = "https://np.tritondigital.com/public/nowplaying?mountName="
                       + Uri.EscapeDataString(s.IdMeta) + "&numberToFetch=1&eventType=track";

            string xml = Recuperer(url);
            if (xml == null) return;

            string artiste = Propriete(xml, "track_artist_name");
            string titre = Propriete(xml, "cue_title");
            string cover = Propriete(xml, "track_cover_url");

            if (titre.Length == 0 && artiste.Length == 0) return;
            Publier(s, artiste, titre, cover);
        }

        /// <summary>Extrait une propriete du XML Triton, sans analyseur XML complet.</summary>
        private static string Propriete(string xml, string nom)
        {
            string ouvre = "property name=\"" + nom + "\"><![CDATA[";
            int d = xml.IndexOf(ouvre, StringComparison.Ordinal);
            if (d < 0) return "";
            d += ouvre.Length;
            int f = xml.IndexOf("]]>", d, StringComparison.Ordinal);
            if (f < d) return "";
            return xml.Substring(d, f - d).Trim();
        }

        // ==================================================================
        //  Source ICY
        // ==================================================================
        private void Icy(Station s)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(s.Url);
            req.Timeout = 10000;
            req.ReadWriteTimeout = 10000;
            req.UserAgent = "WorldRadio/1.0";
            req.Headers.Add("Icy-MetaData", "1");

            using (WebResponse rep = req.GetResponse())
            {
                int intervalle = 0;
                foreach (string cle in rep.Headers.AllKeys)
                {
                    if (!cle.Equals("icy-metaint", StringComparison.OrdinalIgnoreCase)) continue;
                    int.TryParse(rep.Headers[cle], out intervalle);
                }
                if (intervalle <= 0) return;      // station sans metadonnees

                using (Stream flux = rep.GetResponseStream())
                {
                    if (flux == null) return;
                    string brut = LireBloc(flux, intervalle);
                    if (brut == null) return;

                    string titreFlux = ExtraireStreamTitle(brut);
                    if (titreFlux == null) return;

                    string artiste, titre;
                    Decouper(titreFlux, out artiste, out titre);
                    if (titre.Length == 0 && artiste.Length == 0) return;

                    Publier(s, artiste, titre, null);
                }
            }
        }

        /// <summary>Saute l'audio jusqu'au premier bloc de metadonnees et le renvoie.</summary>
        private static string LireBloc(Stream flux, int intervalle)
        {
            byte[] poubelle = new byte[8192];
            int reste = intervalle;
            while (reste > 0)
            {
                int lus = flux.Read(poubelle, 0, Math.Min(reste, poubelle.Length));
                if (lus <= 0) return null;
                reste -= lus;
            }

            byte[] lg = new byte[1];
            if (flux.Read(lg, 0, 1) != 1) return null;

            int taille = lg[0] * 16;
            if (taille <= 0) return null;         // bloc vide : titre inchange

            byte[] buf = new byte[taille];
            int pos = 0;
            while (pos < taille)
            {
                int lus = flux.Read(buf, pos, taille - pos);
                if (lus <= 0) break;
                pos += lus;
            }
            return Decoder(buf, pos);
        }

        /// <summary>
        /// Les serveurs ICY n'annoncent pas leur encodage : certains emettent
        /// de l'UTF-8, d'autres du Latin-1. On tente l'UTF-8 en mode strict,
        /// et on retombe sur Latin-1 si les octets ne sont pas valides.
        /// </summary>
        private static string Decoder(byte[] buf, int longueur)
        {
            try
            {
                UTF8Encoding strict = new UTF8Encoding(false, true);
                return strict.GetString(buf, 0, longueur).TrimEnd('\0').Trim();
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding(28591).GetString(buf, 0, longueur).TrimEnd('\0').Trim();
            }
        }

        private static string ExtraireStreamTitle(string brut)
        {
            const string cle = "StreamTitle='";
            int d = brut.IndexOf(cle, StringComparison.Ordinal);
            if (d < 0) return null;
            d += cle.Length;

            // le titre de Cidade FM contient du XML avec des apostrophes :
            // on s'arrete sur la sequence de fin "';" plutot que sur la
            // premiere apostrophe venue
            int f = brut.IndexOf("';", d, StringComparison.Ordinal);
            if (f < 0) f = brut.LastIndexOf('\'');
            if (f < d) return null;

            return brut.Substring(d, f - d).Trim();
        }

        /// <summary>
        /// Certaines stations collent un identifiant interne apres le titre :
        /// Skyrock diffuse "Bouss - Cote a la hausse §7684753". On retire ce
        /// suffixe, qui n'a aucun sens pour l'auditeur.
        /// </summary>
        private static string Nettoyer(string texte)
        {
            if (string.IsNullOrEmpty(texte)) return texte;

            for (int i = texte.Length - 1; i >= 0; i--)
            {
                char c = texte[i];
                if (c >= '0' && c <= '9') continue;          // on remonte les chiffres
                if (i == texte.Length - 1) break;            // pas de chiffres en fin
                if (c == '§' || c == '#')               // le marqueur, § ou #
                {
                    // le marqueur doit etre precede d'un espace, sinon il fait
                    // partie du titre
                    if (i == 0 || texte[i - 1] == ' ') return texte.Substring(0, i).TrimEnd();
                }
                break;
            }
            return texte;
        }

        private static void Decouper(string titreFlux, out string artiste, out string titre)
        {
            artiste = "";
            titre = "";
            if (string.IsNullOrEmpty(titreFlux)) return;
            titreFlux = Nettoyer(titreFlux);

            // format XML Dalet, utilise par Cidade FM, M80 et Radio Comercial
            if (titreFlux.IndexOf("<DB_DALET_ARTIST_NAME>", StringComparison.Ordinal) >= 0)
            {
                artiste = EntreBalises(titreFlux, "DB_DALET_ARTIST_NAME");
                titre = EntreBalises(titreFlux, "DB_DALET_TITLE_NAME");
                if (titre.Length > 0 || artiste.Length > 0) return;

                // a defaut de morceau, l'emission en cours
                titre = EntreBalises(titreFlux, "SHOW_NAME");
                return;
            }

            // format iHeart, utilise par Z100 et KIIS FM
            if (IHeart(titreFlux, out artiste, out titre)) return;

            int sep = titreFlux.IndexOf(" - ", StringComparison.Ordinal);
            if (sep > 0)
            {
                artiste = titreFlux.Substring(0, sep).Trim();
                titre = titreFlux.Substring(sep + 3).Trim();
                return;
            }

            titre = titreFlux;
        }

        /// <summary>
        /// Les deux formes que prennent les flux iHeart. Sans elles, le titre
        /// partait a l'ecran suivi de toute la fiche technique du morceau :
        ///
        ///   Tame Impala / Jennie - text="Dracula" song_spot="M" MediaBaseId="3164232" ...
        ///   title="Good For You",artist="SELENA GOMEZ",url="song_spot="F" ...
        ///
        /// La premiere met l'artiste devant, le titre entre guillemets apres
        /// « text= » ; la seconde nomme les deux champs.
        /// </summary>
        private static bool IHeart(string t, out string artiste, out string titre)
        {
            artiste = "";
            titre = "";
            if (string.IsNullOrEmpty(t)) return false;

            // « artist=" » et non « amgArtistId=" » : le signe egal suit
            // immediatement le mot, ce qui ecarte les champs voisins.
            int it = t.IndexOf("title=\"", StringComparison.Ordinal);
            int ia = t.IndexOf("artist=\"", StringComparison.Ordinal);
            if (it >= 0 && ia >= 0)
            {
                titre = Guillemets(t, it + 7);
                artiste = Guillemets(t, ia + 8);
                return titre.Length > 0 || artiste.Length > 0;
            }

            int ix = t.IndexOf(" - text=\"", StringComparison.Ordinal);
            if (ix > 0)
            {
                artiste = t.Substring(0, ix).Trim();
                titre = Guillemets(t, ix + 9);
                return titre.Length > 0 || artiste.Length > 0;
            }

            return false;
        }

        /// <summary>Contenu jusqu'au guillemet fermant.</summary>
        private static string Guillemets(string s, int debut)
        {
            if (debut < 0 || debut >= s.Length) return "";
            int fin = s.IndexOf('"', debut);
            if (fin < 0) return s.Substring(debut).Trim();
            return s.Substring(debut, fin - debut).Trim();
        }

        private static string EntreBalises(string source, string balise)
        {
            string ouvre = "<" + balise + ">";
            string ferme = "</" + balise + ">";
            int d = source.IndexOf(ouvre, StringComparison.Ordinal);
            if (d < 0) return "";
            d += ouvre.Length;
            int f = source.IndexOf(ferme, d, StringComparison.Ordinal);
            if (f < d) return "";
            return source.Substring(d, f - d).Trim();
        }

        // ==================================================================
        //  Reseau et cache de pochettes
        // ==================================================================
        private static string Recuperer(string url)
        {
            try
            {
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
                r.Timeout = 8000;
                r.ReadWriteTimeout = 8000;
                r.UserAgent = "WorldRadio/1.0";
                using (WebResponse rep = r.GetResponse())
                using (StreamReader sr = new StreamReader(rep.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch (WebException)
            {
                // Un delai d attente sur le point d acces des titres n est pas
                // une panne : le flux audio, lui, continue. On ne salit pas le
                // journal pour autant, la prochaine interrogation reessaiera.
                return null;
            }
            catch (Exception ex) { Journal.Erreur("appel de " + url, ex); return null; }
        }

    }
}
