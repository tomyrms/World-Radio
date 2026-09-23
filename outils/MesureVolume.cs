// ============================================================================
//  MesureVolume  -  loudness integree de chaque station (ITU-R BS.1770)
// ----------------------------------------------------------------------------
//  Ecoute chaque flux en parallele, ignore les premieres secondes (les flux
//  Triton et iHeart ouvrent souvent sur une publicite de pre-roll, mixee a un
//  autre niveau que l'antenne), puis mesure :
//
//    LUFS   loudness integree, ponderation K, fenetres de 400 ms recouvrantes
//           a 75 %, porte absolue a -70 LUFS et porte relative a -10 LU.
//           C'est la mesure qu'utilisent les diffuseurs eux-memes.
//    RMS    niveau efficace brut, sans ponderation, pour comparaison
//    Crete  echantillon le plus fort, pour savoir s'il reste de la marge
//
//  Usage :  MesureVolume.exe <fichier.ini> <secondes ignorees> <secondes mesurees>
//  Sortie :  une ligne par station, champs separes par |
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NAudio.MediaFoundation;
using NAudio.Wave;

sealed class Biquad
{
    readonly double b0, b1, b2, a1, a2;
    double z1, z2;

    public Biquad(double b0, double b1, double b2, double a1, double a2)
    { this.b0 = b0; this.b1 = b1; this.b2 = b2; this.a1 = a1; this.a2 = a2; }

    // forme directe II transposee
    public double Filtrer(double x)
    {
        double y = b0 * x + z1;
        z1 = b1 * x - a1 * y + z2;
        z2 = b2 * x - a2 * y;
        return y;
    }

    // Etage 1 de la ponderation K : plateau haut de +4 dB. Les constantes sont
    // celles qui redonnent exactement les coefficients publies a 48 kHz, ce
    // qui permet de les recalculer a 44,1 kHz sans approximation.
    public static Biquad Plateau(double fs)
    {
        double G = 3.999843853973347, Q = 0.7071752369554196, fc = 1681.974450955533;
        double K = Math.Tan(Math.PI * fc / fs);
        double Vh = Math.Pow(10.0, G / 20.0);
        double Vb = Math.Pow(Vh, 0.4996667741545416);
        double a0 = 1.0 + K / Q + K * K;
        return new Biquad((Vh + Vb * K / Q + K * K) / a0,
                          2.0 * (K * K - Vh) / a0,
                          (Vh - Vb * K / Q + K * K) / a0,
                          2.0 * (K * K - 1.0) / a0,
                          (1.0 - K / Q + K * K) / a0);
    }

    // Etage 2 : passe-haut, pour que les infra-basses ne pesent pas.
    public static Biquad PasseHaut(double fs)
    {
        double Q = 0.5003270373238773, fc = 38.13547087602444;
        double K = Math.Tan(Math.PI * fc / fs);
        double a0 = 1.0 + K / Q + K * K;
        return new Biquad(1.0, -2.0, 1.0,
                          2.0 * (K * K - 1.0) / a0,
                          (1.0 - K / Q + K * K) / a0);
    }
}

sealed class Station
{
    public int Numero;
    public string Nom = "";
    public string Url = "";
    public bool Active = true;
    public double GainDb;

    public double Lufs = double.NaN, Rms = double.NaN, Crete = double.NaN;
    public double Secondes;
    public int Frequence, Canaux;
    public string Etat = "pas de reponse";
}

static class Programme
{
    // Si renseigne, chaque flux passe par le gain de sa station puis par le
    // limiteur du mod, charge depuis son DLL : on mesure ce qui sortira
    // reellement, pas une copie du code.
    static Type _limiteur;

    static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage : MesureVolume <ini> <ignorer_s> <mesurer_s>");
            return 2;
        }
        double ignorer = double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
        double mesurer = double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);

        List<Station> stations = LireIni(args[0]);
        if (args.Length >= 4)
        {
            System.Reflection.Assembly mod = System.Reflection.Assembly.LoadFrom(args[3]);
            _limiteur = mod.GetType("WorldRadio.Limiteur", true);
        }
        MediaFoundationApi.Startup();

        List<Thread> fils = new List<Thread>();
        foreach (Station s in stations)
        {
            if (!s.Active) { s.Etat = "desactivee"; continue; }
            Station st = s;
            Thread t = new Thread(() => Mesurer(st, ignorer, mesurer));
            t.IsBackground = true;
            t.Start();
            fils.Add(t);
        }

        // Un flux qui cale ne doit pas bloquer les autres : chacun a son delai,
        // largement au-dela de la duree prevue.
        DateTime limite = DateTime.UtcNow.AddSeconds(ignorer + mesurer + 45);
        foreach (Thread t in fils)
        {
            int reste = (int)Math.Max(0, (limite - DateTime.UtcNow).TotalMilliseconds);
            t.Join(reste);
        }

        foreach (Station s in stations)
        {
            Console.WriteLine(string.Join("|", new string[] {
                s.Numero.ToString(), s.Nom,
                F(s.Lufs), F(s.Rms), F(s.Crete),
                s.Secondes.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                s.Frequence.ToString(), s.Canaux.ToString(), s.Etat }));
        }
        return 0;
    }

    static string F(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return "";
        return v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    }

    static void Mesurer(Station s, double ignorer, double mesurer)
    {
        try
        {
            s.Etat = "connexion";
            using (MediaFoundationReader lecteur = new MediaFoundationReader(s.Url))
            {
                ISampleProvider src = lecteur.ToSampleProvider();
                if (_limiteur != null)
                {
                    NAudio.Wave.SampleProviders.VolumeSampleProvider gain =
                        new NAudio.Wave.SampleProviders.VolumeSampleProvider(src);
                    gain.Volume = (float)Math.Pow(10.0, s.GainDb / 20.0);
                    src = (ISampleProvider)Activator.CreateInstance(_limiteur,
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                        null, new object[] { gain }, null);
                }
                int fs = src.WaveFormat.SampleRate;
                int ch = src.WaveFormat.Channels;
                s.Frequence = fs; s.Canaux = ch;

                Biquad[] plateau = new Biquad[ch];
                Biquad[] passeHaut = new Biquad[ch];
                for (int c = 0; c < ch; c++) { plateau[c] = Biquad.Plateau(fs); passeHaut[c] = Biquad.PasseHaut(fs); }

                float[] buf = new float[fs * ch / 10];         // 100 ms
                long aIgnorer = (long)(ignorer * fs);
                long aMesurer = (long)(mesurer * fs);
                long lus = 0;

                // energie ponderee K par sous-bloc de 100 ms, canaux additionnes
                List<double> sousBlocs = new List<double>();
                int parSousBloc = fs / 10;
                double energieCourante = 0; int dansSousBloc = 0;
                double sommeBrute = 0; long nBrut = 0; double crete = 0;

                s.Etat = "ecoute";
                while (lus < aIgnorer + aMesurer)
                {
                    int n = src.Read(buf, 0, buf.Length);
                    if (n <= 0) { Thread.Sleep(20); continue; }
                    int trames = n / ch;
                    for (int i = 0; i < trames; i++, lus++)
                    {
                        bool compte = lus >= aIgnorer;
                        double e = 0;
                        for (int c = 0; c < ch; c++)
                        {
                            double x = buf[i * ch + c];
                            double k = passeHaut[c].Filtrer(plateau[c].Filtrer(x));
                            if (compte)
                            {
                                e += k * k;
                                sommeBrute += x * x; nBrut++;
                                double ax = Math.Abs(x);
                                if (ax > crete) crete = ax;
                            }
                        }
                        if (!compte) continue;

                        // Un flux mono est restitue sur les deux enceintes : on
                        // le compte deux fois, comme on l'entend.
                        if (ch == 1) e *= 2.0;

                        energieCourante += e;
                        if (++dansSousBloc == parSousBloc)
                        {
                            sousBlocs.Add(energieCourante);
                            energieCourante = 0; dansSousBloc = 0;
                        }
                    }
                }

                s.Secondes = sousBlocs.Count / 10.0;
                s.Rms = 10.0 * Math.Log10(sommeBrute / Math.Max(1, nBrut));
                s.Crete = 20.0 * Math.Log10(Math.Max(crete, 1e-9));
                s.Lufs = Integrer(sousBlocs, parSousBloc);
                s.Etat = "ok";
            }
        }
        catch (Exception ex)
        {
            s.Etat = "erreur: " + ex.GetType().Name + " " + ex.Message.Replace('|', '/').Replace('\n', ' ');
        }
    }

    // Blocs de 400 ms recouvrants a 75 % : quatre sous-blocs de 100 ms.
    static double Integrer(List<double> sb, int parSousBloc)
    {
        List<double> z = new List<double>();
        for (int j = 0; j + 4 <= sb.Count; j++)
            z.Add((sb[j] + sb[j + 1] + sb[j + 2] + sb[j + 3]) / (4.0 * parSousBloc));
        if (z.Count == 0) return double.NaN;

        // porte absolue
        List<double> abs = new List<double>();
        foreach (double v in z) if (L(v) > -70.0) abs.Add(v);
        if (abs.Count == 0) return double.NaN;

        // porte relative : 10 LU sous la moyenne des blocs retenus
        double seuil = L(Moyenne(abs)) - 10.0;
        List<double> rel = new List<double>();
        foreach (double v in abs) if (L(v) > seuil) rel.Add(v);
        if (rel.Count == 0) return double.NaN;

        return L(Moyenne(rel));
    }

    static double L(double z) { return -0.691 + 10.0 * Math.Log10(Math.Max(z, 1e-12)); }

    static double Moyenne(List<double> l)
    {
        double s = 0; foreach (double v in l) s += v; return s / l.Count;
    }

    static List<Station> LireIni(string chemin)
    {
        List<Station> res = new List<Station>();
        Station cour = null;
        foreach (string brut in File.ReadAllLines(chemin))
        {
            string l = brut.Trim();
            if (l.Length == 0 || l.StartsWith(";")) continue;
            if (l.StartsWith("["))
            {
                cour = null;
                if (l.StartsWith("[Station", StringComparison.OrdinalIgnoreCase))
                {
                    int num;
                    if (int.TryParse(l.Substring(8).TrimEnd(']'), out num))
                    { cour = new Station(); cour.Numero = num; res.Add(cour); }
                }
                continue;
            }
            if (cour == null) continue;
            int eq = l.IndexOf('=');
            if (eq <= 0) continue;
            string k = l.Substring(0, eq).Trim(), v = l.Substring(eq + 1).Trim();
            if (k.Equals("Name", StringComparison.OrdinalIgnoreCase)) cour.Nom = v;
            else if (k.Equals("Url", StringComparison.OrdinalIgnoreCase)) cour.Url = v;
            else if (k.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
                cour.Active = !v.Equals("false", StringComparison.OrdinalIgnoreCase);
            else if (k.Equals("Gain", StringComparison.OrdinalIgnoreCase))
                double.TryParse(v, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out cour.GainDb);
        }
        return res;
    }
}
