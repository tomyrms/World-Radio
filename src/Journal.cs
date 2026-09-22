// ============================================================================
//  Journal  -  trace de diagnostic
// ----------------------------------------------------------------------------
//  Un mod GTA n'a pas de console : sans trace ecrite, un echec est muet et
//  impossible a diagnostiquer autrement qu'en devinant. Tout ce qui peut
//  echouer ecrit donc ici.
// ============================================================================

using System;
using System.IO;
using System.Text;

namespace WorldRadio
{
    internal static class Journal
    {
        private static readonly object _verrou = new object();
        private static string _fichier;

        /// <summary>Fixe l'emplacement du journal ; jusque-la, tout est garde en memoire.</summary>
        internal static void Ouvrir(string dossier)
        {
            lock (_verrou)
            {
                _fichier = Path.Combine(dossier, "WorldRadio.log");
                try { File.WriteAllText(_fichier, "", Encoding.UTF8); }
                catch { _fichier = null; }
            }
            Ecrire("--- World Radio demarre ---");
        }

        private static string _dernier;
        private static int _repetitions;

        /// <summary>
        /// Les messages identiques consecutifs sont comptes, pas reecrits.
        ///
        /// Sans cela, une boucle en echec ecrit une ligne par image : un defaut
        /// de lecture de la manette a deja produit 191 681 lignes en quelques
        /// minutes, avec l'acces disque correspondant a chaque image.
        /// </summary>
        internal static void Ecrire(string message)
        {
            lock (_verrou)
            {
                if (_fichier == null) return;

                if (message == _dernier)
                {
                    _repetitions++;
                    return;
                }

                if (_repetitions > 0)
                {
                    Poser("   (ligne precedente repetee " + _repetitions + " fois)");
                    _repetitions = 0;
                }
                _dernier = message;
                Poser(message);
            }
        }

        private static void Poser(string message)
        {
            try
            {
                File.AppendAllText(_fichier,
                    DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { }
        }

        internal static void Erreur(string contexte, Exception ex)
        {
            Ecrire("ERREUR " + contexte + " -> " + ex.GetType().Name + " : " + ex.Message);
        }
    }
}
