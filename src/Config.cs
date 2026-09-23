// ============================================================================
//  Config  -  lecture et ecriture de WorldRadio.ini
// ----------------------------------------------------------------------------
//  Le volume regle en jeu est reecrit dans le fichier, pour que le niveau
//  choisi a l'oreille survive au redemarrage.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Keys = System.Windows.Forms.Keys;

namespace WorldRadio
{
    internal sealed class Station
    {
        public string Nom;
        public string Url;
        public string Icone;
        public string NomPays;

        /// <summary>Source du titre en cours : flux ICY, point d acces Triton, ou rien.</summary>
        public SourceMeta Source = SourceMeta.Icy;

        /// <summary>Identifiant cote source : nom de mount pour Triton.</summary>
        public string IdMeta = "";

        /// <summary>Position dans la liste plate, qui sert d'identifiant stable.</summary>
        public int Index;

        /// <summary>
        /// Correction de niveau, en decibels, pour que toutes les stations
        /// sonnent aussi fort. Les radios ne sont pas masterisees au meme
        /// niveau : mesure faite, Skyrock sort 11 LU plus fort que HOT 97,
        /// soit quatre fois plus fort a l'oreille.
        /// </summary>
        public double GainDb;
    }

    /// <summary>
    /// Premier niveau du menu. Regrouper par pays evite un menu ou douze
    /// stations se disputent la place, et permet d'en ajouter d'autres sans
    /// toucher au code.
    /// </summary>
    internal sealed class Pays
    {
        public string Nom;
        public string Drapeau;
        public readonly List<Station> Stations = new List<Station>();
    }

    internal sealed class Config
    {
        // --- general ---
        public double Volume = 1.00;
        public Keys ToucheSuivante = Keys.F10;
        public Keys TouchePrecedente = Keys.F9;
        public Keys ToucheVolPlus = Keys.NumPad9;
        public Keys ToucheVolMoins = Keys.NumPad3;
        public Keys ToucheCoupure = Keys.NumPad0;


        public bool JouerEnPause;
        public bool JouerEnArrierePlan;
        public bool CouperRadioJeu = true;
        public bool SuivreReglagesJeu = true;
        /// <summary>Auto, Toujours, ou Desactive.</summary>
        public ModeApercu Apercu = ModeApercu.Auto;
        public double SecondesApercu = 5.0;

        /// <summary>
        /// De combien l'encart remonte depuis le bas de l'ecran, en unites de
        /// dessin. GTA affiche le nom du quartier dans ce meme coin : pose sur
        /// la marge de securite seule, l'encart lui passait dessus.
        /// Reglable, car la place occupee par ce nom depend de la resolution.
        /// </summary>
        public double HauteurApercu = 70.0;

        /// <summary>
        /// Echelle du temps pendant que la roue est ouverte. La roue d'origine
        /// de GTA ralentit le jeu pour qu'on ne choisisse pas sa station en
        /// pleine circulation. 1,00 desactive l'effet.
        /// </summary>
        public double Ralenti = 0.30;

        /// <summary>
        /// Ce qu'il reste du volume pendant une mission, un dialogue ou une
        /// cinematique. 0,30 = soixante-dix pour cent de moins. 1,00 desactive
        /// l'attenuation, 0,00 coupe completement.
        /// </summary>
        public double Attenuation = 0.30;

        /// <summary>
        /// Faire aussi baisser la radio sur les repliques d'ambiance des PNJ.
        /// Desactive par defaut, et a raison : un passant qui rale suffirait a
        /// faire plonger la musique toutes les dix secondes.
        /// </summary>
        public bool RepliquesAmbiantes;
        public bool AxeMenuInverse;

        /// <summary>Trace detaillee du volume et de la pause, pour diagnostic.</summary>
        public bool JournalAudio;

        /// <summary>
        /// Derniere station ecoutee, -1 si aucune. Elle n est PAS relancee au
        /// demarrage : le mod s allumerait tout seul, ce qui serait une
        /// surprise. Elle sert a positionner le menu, pour qu une seule
        /// poussee suffise a la reprendre.
        /// </summary>
        public int DerniereStation = -1;

        /// <summary>Liste plate, dans l'ordre du fichier : l'index sert d'identifiant.</summary>
        public readonly List<Station> Stations = new List<Station>();

        /// <summary>Regroupement par pays, dans l'ordre de la section [Pays].</summary>
        public readonly List<Pays> Pays = new List<Pays>();

        /// <summary>Dossier "scripts", ou vivent le .ini et le dossier d'icones.</summary>
        public string Dossier;

        public string CheminIni { get { return Path.Combine(Dossier, "WorldRadio.ini"); } }
        public string DossierIcones { get { return Path.Combine(Dossier, "WorldRadio"); } }

        // ------------------------------------------------------------------

        internal static Config Charger(string dossier)
        {
            Config c = new Config();
            c.Dossier = dossier;

            string ini = c.CheminIni;
            if (!File.Exists(ini))
            {
                Journal.Ecrire("WorldRadio.ini introuvable dans " + dossier);
                return c;
            }

            string section = "";
            Dictionary<string, string> courante = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            // valide la section qui vient de se terminer
            Action valider = delegate
            {
                if (section.Equals("Pays", StringComparison.OrdinalIgnoreCase))
                    c.LirePays(courante, ini);
                else if (section.StartsWith("Station", StringComparison.OrdinalIgnoreCase))
                    c.AjouterStation(courante);
                else if (section.Equals("General", StringComparison.OrdinalIgnoreCase))
                    c.LireGeneral(courante);
                courante.Clear();
            };

            foreach (string brut in File.ReadAllLines(ini))
            {
                string l = brut.Trim();
                if (l.Length == 0 || l[0] == ';' || l[0] == '#') continue;

                if (l[0] == '[' && l[l.Length - 1] == ']')
                {
                    valider();
                    section = l.Substring(1, l.Length - 2).Trim();
                    continue;
                }

                int eq = l.IndexOf('=');
                if (eq <= 0) continue;
                courante[l.Substring(0, eq).Trim()] = l.Substring(eq + 1).Trim();
            }
            valider();

            Journal.Ecrire("config : " + c.Stations.Count + " station(s) dans "
                           + c.Pays.Count + " pays, volume "
                           + c.Volume.ToString("0.###", CultureInfo.InvariantCulture)
                           );
            foreach (Station s in c.Stations)
                Journal.Ecrire("   " + s.Nom + "  ->  " + s.Url);

            c.VerifierImages();

            return c;
        }

        /// <summary>
        /// La section [Pays] fixe l'ordre du premier niveau et associe un
        /// drapeau a chaque pays. Un dictionnaire ne conserve pas l'ordre
        /// d'ecriture, on relit donc le fichier pour le retrouver.
        /// </summary>
        private void LirePays(Dictionary<string, string> d, string ini)
        {
            if (d.Count == 0) return;
            bool dansSection = false;

            foreach (string brut in File.ReadAllLines(ini))
            {
                string l = brut.Trim();
                if (l.Length == 0 || l[0] == ';' || l[0] == '#') continue;

                if (l[0] == '[' && l[l.Length - 1] == ']')
                {
                    dansSection = l.Substring(1, l.Length - 2).Trim()
                                   .Equals("Pays", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!dansSection) continue;

                int eq = l.IndexOf('=');
                if (eq <= 0) continue;
                string nom = l.Substring(0, eq).Trim();
                if (nom.Length == 0 || Trouver(nom) != null) continue;

                Pays p = new Pays();
                p.Nom = nom;
                p.Drapeau = l.Substring(eq + 1).Trim();
                Pays.Add(p);
            }
        }

        /// <summary>
        /// Signale les images declarees mais absentes. Le mod fonctionne sans,
        /// mais l emplacement reste vide a l ecran : autant que le journal le
        /// dise plutot que de laisser chercher.
        /// </summary>
        private void VerifierImages()
        {
            int manquantes = 0;
            foreach (Pays p in Pays)
            {
                if (!string.IsNullOrEmpty(p.Drapeau) && !Existe(p.Drapeau))
                {
                    Journal.Ecrire("drapeau absent : " + p.Drapeau + "  (" + p.Nom + ")");
                    manquantes++;
                }
            }
            foreach (Station s in Stations)
            {
                if (!string.IsNullOrEmpty(s.Icone) && !Existe(s.Icone))
                {
                    Journal.Ecrire("logo absent : " + s.Icone + "  (" + s.Nom + ")");
                    manquantes++;
                }
            }
            if (manquantes == 0) Journal.Ecrire("images : toutes presentes");
        }

        private bool Existe(string fichier)
        {
            try { return File.Exists(Path.Combine(DossierIcones, fichier)); }
            catch { return false; }
        }

        private Pays Trouver(string nom)
        {
            foreach (Pays p in Pays)
                if (p.Nom.Equals(nom, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        private void AjouterStation(Dictionary<string, string> d)
        {
            if (!Lire(d, "Enabled", "true").Equals("true", StringComparison.OrdinalIgnoreCase)) return;

            string nom = Lire(d, "Name", null);
            string url = Lire(d, "Url", null);
            if (string.IsNullOrEmpty(nom) || string.IsNullOrEmpty(url)) return;

            Station s = new Station();
            s.Nom = nom;
            s.Url = url;
            s.Icone = Lire(d, "Icon", "");
            s.NomPays = Lire(d, "Country", "");
            s.IdMeta  = Lire(d, "MetadataId", "");

            // Borne : au-dela de +6 dB, une erreur de mesure deviendrait une
            // saturation ; en-deca de -24 dB, la station serait inaudible.
            double gain;
            if (double.TryParse(Lire(d, "Gain", "0"), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out gain))
                s.GainDb = Math.Max(-24.0, Math.Min(6.0, gain));

            string mode = Lire(d, "Metadata", "Icy");
            if (mode.Equals("Triton", StringComparison.OrdinalIgnoreCase)) s.Source = SourceMeta.Triton;
            else if (mode.Equals("None", StringComparison.OrdinalIgnoreCase)) s.Source = SourceMeta.Aucune;
            else s.Source = SourceMeta.Icy;
            s.Index = Stations.Count;
            Stations.Add(s);

            // un pays absent de la section [Pays] est cree a la volee, sans
            // drapeau : la station reste accessible malgre l'oubli
            if (s.NomPays.Length == 0) s.NomPays = "AUTRES";
            Pays p = Trouver(s.NomPays);
            if (p == null)
            {
                p = new Pays();
                p.Nom = s.NomPays;
                p.Drapeau = "";
                Pays.Add(p);
                Journal.Ecrire("pays sans drapeau declare : " + s.NomPays);
            }
            p.Stations.Add(s);
        }

        private void LireGeneral(Dictionary<string, string> d)
        {
            double v;
            if (double.TryParse(Lire(d, "Volume", "1.00"), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out v))
                Volume = Borner(v);

            ToucheSuivante    = Touche(d, "NextStationKey", ToucheSuivante);
            TouchePrecedente  = Touche(d, "PrevStationKey", TouchePrecedente);
            ToucheVolPlus     = Touche(d, "VolumeUpKey", ToucheVolPlus);
            ToucheVolMoins    = Touche(d, "VolumeDownKey", ToucheVolMoins);
            ToucheCoupure     = Touche(d, "MuteKey", ToucheCoupure);


            JouerEnPause      = Booleen(d, "PlayInPauseMenu", JouerEnPause);
            JouerEnArrierePlan= Booleen(d, "PlayWhileInBackground", JouerEnArrierePlan);
            CouperRadioJeu    = Booleen(d, "MuteGameRadio", CouperRadioJeu);
            SuivreReglagesJeu = Booleen(d, "FollowGameAudioSettings", SuivreReglagesJeu);
            AxeMenuInverse    = Booleen(d, "InvertMenuAxis", AxeMenuInverse);
            JournalAudio      = Booleen(d, "AudioLog", JournalAudio);

            int ds;
            if (int.TryParse(Lire(d, "LastStation", "-1"), out ds)) DerniereStation = ds;

            string ap = Lire(d, "NowPlaying", "Auto");
            if (ap.Equals("Always", StringComparison.OrdinalIgnoreCase)) Apercu = ModeApercu.Toujours;
            else if (ap.Equals("Off", StringComparison.OrdinalIgnoreCase)) Apercu = ModeApercu.Desactive;
            else Apercu = ModeApercu.Auto;

            double sa;
            if (double.TryParse(Lire(d, "NowPlayingSeconds", "5"), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out sa) && sa >= 0.0)
                SecondesApercu = sa;

            double ho;
            if (double.TryParse(Lire(d, "NowPlayingRaise", "70"), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out ho) && ho >= 0.0 && ho <= 400.0)
                HauteurApercu = ho;

            double ra;
            if (double.TryParse(Lire(d, "WheelSlowMotion", "0.30"), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out ra) && ra >= 0.05 && ra <= 1.0)
                Ralenti = ra;

            double at;
            if (double.TryParse(Lire(d, "DuckDuringDialogue", "0.30"), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out at) && at >= 0.0 && at <= 1.0)
                Attenuation = at;

            RepliquesAmbiantes = Lire(d, "DuckOnAmbientSpeech", "false")
                                 .Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        internal static double Borner(double v)
        {
            return Math.Max(0.0, Math.Min(1.0, v));
        }

        private static string Lire(Dictionary<string, string> d, string cle, string defaut)
        {
            string v;
            return d.TryGetValue(cle, out v) ? v : defaut;
        }

        private static bool Booleen(Dictionary<string, string> d, string cle, bool defaut)
        {
            string v = Lire(d, cle, null);
            if (v == null) return defaut;
            if (v.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (v.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return defaut;
        }

        private static Keys Touche(Dictionary<string, string> d, string cle, Keys defaut)
        {
            string v = Lire(d, cle, null);
            if (v == null) return defaut;
            try { return (Keys)Enum.Parse(typeof(Keys), v, true); }
            catch { Journal.Ecrire("touche invalide pour " + cle + " : " + v); return defaut; }
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Memorise la station, pour y revenir a la prochaine session.
        /// L'extinction n'est PAS memorisee : retenir "eteint" ferait perdre
        /// la station a laquelle on tenait, et le menu rouvrirait en haut de
        /// liste au lieu du dernier choix.
        /// </summary>
        internal void EnregistrerDerniereStation(int index)
        {
            if (index < 0 || index >= Stations.Count) return;
            if (index == DerniereStation) return;
            DerniereStation = index;
            EcrireCle("LastStation", index.ToString(CultureInfo.InvariantCulture));
        }

        internal void EnregistrerVolume(double v)
        {
            EcrireCle("Volume", v.ToString("0.00", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Reecrit une cle du .ini en preservant tout le reste : commentaires,
        /// ordre, mise en page. La cle est ajoutee a la section [General] si
        /// elle n'y figure pas encore.
        ///
        /// La comparaison se fait sur le nom exact suivi de "=", sinon
        /// "Volume" capturerait aussi "VolumeUpKey".
        /// </summary>
        private void EcrireCle(string cle, string valeur)
        {
            try
            {
                string ini = CheminIni;
                if (!File.Exists(ini)) return;

                string prefixe = cle + "=";
                List<string> lignes = new List<string>(File.ReadAllLines(ini));

                for (int i = 0; i < lignes.Count; i++)
                {
                    string l = lignes[i].TrimStart();
                    if (l.Length == 0 || l[0] == ';' || l[0] == '#') continue;
                    if (!l.StartsWith(prefixe, StringComparison.OrdinalIgnoreCase)) continue;

                    lignes[i] = prefixe + valeur;
                    File.WriteAllLines(ini, lignes.ToArray());
                    return;
                }

                // cle absente : on l'ajoute juste apres l'en-tete [General]
                for (int i = 0; i < lignes.Count; i++)
                {
                    if (!lignes[i].Trim().Equals("[General]", StringComparison.OrdinalIgnoreCase)) continue;
                    lignes.Insert(i + 1, prefixe + valeur);
                    File.WriteAllLines(ini, lignes.ToArray());
                    return;
                }
            }
            catch (Exception ex) { Journal.Erreur("ecriture de " + cle, ex); }
        }
    }
}
