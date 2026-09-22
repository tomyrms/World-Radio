// ============================================================================
//  Systeme  -  localisation du dossier scripts, focus fenetre, chargement NAudio
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WorldRadio
{
    internal static class Dossiers
    {
        private static string _scripts;

        /// <summary>
        /// Le dossier "scripts" ne se deduit pas de AppDomain.BaseDirectory a
        /// l'interieur du processus du jeu : celui-ci pointe ailleurs. On part
        /// donc de l'executable, avec des recours en cascade.
        /// </summary>
        internal static string Scripts()
        {
            if (_scripts != null) return _scripts;

            foreach (string candidat in Candidats())
            {
                if (string.IsNullOrEmpty(candidat)) continue;
                try
                {
                    if (!Directory.Exists(candidat)) continue;
                    // un dossier scripts credible contient notre .ini, ou au
                    // moins d'autres mods
                    if (File.Exists(Path.Combine(candidat, "WorldRadio.ini"))
                        || Directory.GetFiles(candidat, "*.dll").Length > 0)
                    {
                        _scripts = candidat;
                        return _scripts;
                    }
                }
                catch { }
            }

            _scripts = Candidats()[0] ?? Directory.GetCurrentDirectory();
            return _scripts;
        }

        /// <summary>Racine du jeu, deduite de l executable.</summary>
        internal static string Jeu()
        {
            try { return Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName); }
            catch { return null; }
        }

        private static string[] Candidats()
        {
            string jeu = null;
            try { jeu = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName); }
            catch { }

            string domaine = null;
            try { domaine = AppDomain.CurrentDomain.BaseDirectory; }
            catch { }

            string assemblage = null;
            try { assemblage = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
            catch { }

            return new string[]
            {
                jeu == null ? null : Path.Combine(jeu, "scripts"),
                assemblage,
                domaine == null ? null : Path.Combine(domaine, "scripts"),
                domaine
            };
        }
    }

    // ------------------------------------------------------------------

    internal static class Fenetre
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr fenetre, out uint pid);

        private static uint _nous;

        /// <summary>Game.IsPaused ne couvre que le menu pause, pas un alt-tab.</summary>
        internal static bool AuPremierPlan()
        {
            if (_nous == 0)
            {
                try { _nous = (uint)Process.GetCurrentProcess().Id; }
                catch { return true; }   // dans le doute, on ne coupe pas
            }

            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return false;

            uint pid;
            GetWindowThreadProcessId(h, out pid);
            return pid == _nous;
        }
    }

    // ------------------------------------------------------------------

    internal static class ChargeurNAudio
    {
        private static bool _fait;

        /// <summary>
        /// ScriptHookVDotNet charge deja NAudio.dll du dossier scripts, mais on
        /// ne s'y fie pas : ce recours doit etre pose AVANT que le moindre type
        /// NAudio soit touche, le JIT resolvant les types a la premiere
        /// compilation de la methode qui les utilise.
        /// </summary>
        internal static void Init(string dossierScripts)
        {
            if (_fait) return;
            _fait = true;
            string dossier = dossierScripts;

            AppDomain.CurrentDomain.AssemblyResolve += delegate(object envoyeur, ResolveEventArgs e)
            {
                try
                {
                    string nom = new AssemblyName(e.Name).Name;
                    if (!nom.Equals("NAudio", StringComparison.OrdinalIgnoreCase)) return null;

                    string p = Path.Combine(dossier, "NAudio.dll");
                    if (!File.Exists(p))
                    {
                        Journal.Ecrire("NAudio.dll absent de " + dossier);
                        return null;
                    }
                    Journal.Ecrire("NAudio resolu depuis " + p);
                    return Assembly.LoadFrom(p);
                }
                catch (Exception ex) { Journal.Erreur("resolution de NAudio", ex); }
                return null;
            };
        }
    }

    // ========================================================================
    //  Proportions  -  dimensions reelles d'une image
    // ------------------------------------------------------------------------
    //  Les logos de stations ne sont PAS carres : « RADIO COMERCIAL » fait 3,7
    //  fois plus large que haut, « rouge » 2,8, et les drapeaux sont en 3:2.
    //  Les dessiner dans un carre les ecrasait, ce qui donnait ces logos
    //  etranges qu'on ne reconnaissait qu'a moitie.
    //
    //  CustomSprite n'expose pas la taille du fichier charge : on lit donc
    //  l'entete du PNG. Sa structure est fixe — signature de 8 octets, puis le
    //  bloc IHDR dont les deux premiers entiers sont largeur et hauteur, en
    //  gros-boutiste.
    //
    //  Volontairement a l'ecart de Dessin, qui reference des types du jeu :
    //  ici, tout est verifiable sans lancer GTA.
    // ========================================================================
    internal static class Proportions
    {
        private static readonly Dictionary<string, float> _ratios =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        private static string _dossier = "";

        internal static void Init(string dossier)
        {
            _dossier = dossier == null ? "" : dossier;
            _ratios.Clear();
        }

        /// <summary>Largeur divisee par hauteur, 1 si la lecture echoue.</summary>
        internal static float Ratio(string fichier)
        {
            if (string.IsNullOrEmpty(fichier)) return 1f;

            float r;
            if (_ratios.TryGetValue(fichier, out r)) return r;

            r = 1f;
            try
            {
                string chemin = Path.Combine(_dossier, fichier);
                using (FileStream f = File.OpenRead(chemin))
                {
                    byte[] e = new byte[24];
                    if (f.Read(e, 0, 24) == 24
                        && e[0] == 0x89 && e[1] == 0x50 && e[2] == 0x4E && e[3] == 0x47)
                    {
                        int l = (e[16] << 24) | (e[17] << 16) | (e[18] << 8) | e[19];
                        int h = (e[20] << 24) | (e[21] << 16) | (e[22] << 8) | e[23];
                        if (l > 0 && h > 0 && l < 20000 && h < 20000) r = (float)l / h;
                    }
                }
            }
            catch (Exception ex) { Journal.Erreur("proportions de " + fichier, ex); }

            _ratios[fichier] = r;
            return r;
        }

        /// <summary>
        /// Dimensions d'une image contenue dans une boite carree, sans la
        /// deformer : le grand cote touche la boite, l'autre est reduit.
        /// </summary>
        internal static void Contenir(string fichier, float boite, out float l, out float h)
        {
            float r = Ratio(fichier);
            if (r >= 1f) { l = boite; h = boite / r; }
            else { h = boite; l = boite * r; }
        }
    }
}
