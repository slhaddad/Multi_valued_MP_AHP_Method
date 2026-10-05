// corrigé le 25/06/2020
// Version V3 : élément structurant (ES) = disque euclidien de rayon i (dx² + dy² <= i²),
//   utilisé directement en une seule passe pour l'érosion, la dilatation, l'ouverture et
//   la fermeture (5, 13, 29, 49, 81 pixels pour i = 1..5, comme skimage.morphology.disk(i)).
//   La reconstruction géodésique utilise le disque élémentaire B1 (rayon 1, 5 pixels).
// Version V4 : [V4-ANTI-CONDORCET] le ∧ / ∨ point à point de la reconstruction utilise un ordre de référence
//   fixe et transitif (score de la méthode contre un panel fixe de vecteurs de l'image), et la suite
//   des marqueurs est rendue monotone : plus de cycle de Condorcet, stabilité toujours atteinte.
//   Le classement des n pixels de la fenêtre (MinMaxVecteurs) est inchangé.
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows;
using SDColor = System.Drawing.Color;
using SDPoint = System.Drawing.Point;

namespace EMP_AHP_UniquePreferenceFunction
{
    public partial class MainWindow : Window
    {
        List<Bitmap> imagesBmp = new List<Bitmap>();
        public MainWindow()
        {
            InitializeComponent();
        }

        // Dossier de sortie « IMAGES-résultat » : toujours juste sous le dossier du projet.
        // On remonte depuis le dossier de l'exécutable (bin\Debug\... ou Executable\) jusqu'au
        // dossier qui contient la solution (.sln) ; s'il n'y en a pas (exécutable copié seul sur
        // une autre machine), le dossier est créé à côté de l'exécutable.
        private static string DossierResultats()
        {
            string dossierExe = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                DirectoryInfo d = new DirectoryInfo(dossierExe);
                while (d != null)
                {
                    if (d.GetFiles("*.sln").Length > 0)
                        return Path.Combine(d.FullName, "IMAGES-résultat");
                    d = d.Parent;
                }
            }
            catch (Exception)
            {
                // Dossier parent illisible : on garde le dossier de l'exécutable
            }
            return Path.Combine(dossierExe, "IMAGES-résultat");
        }

        private void button_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFile = new OpenFileDialog();
            openFile.Multiselect = true;
            openFile.DefaultExt = "png";
            openFile.Filter = "PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|BMP (*.bmp)|*.bmp|TIFF (*.tiff;*.tif)|*.tiff;*.tif";
            bool? ok = openFile.ShowDialog();
            if (ok != true || openFile.FileNames.Length == 0)
                return;
            foreach (Bitmap old in imagesBmp)
                old.Dispose();
            imagesBmp.Clear();
            foreach (string filename in openFile.FileNames)
                imagesBmp.Add(new Bitmap(filename));
            MessageBox.Show(imagesBmp.Count + " bande(s) chargée(s).", "Chargement");
        }

        private static bool TryParseInt(string s, out int v)
        {
            return int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)
                || int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out v);
        }

        private static bool TryParseDouble(string s, out double v)
        {
            string t = (s ?? "").Trim().Replace(',', '.');
            return double.TryParse(t, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out v);
        }

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            if (imagesBmp.Count == 0)
            {
                MessageBox.Show("Chargez d'abord au moins une bande (bouton Parcourir).", "Erreur");
                return;
            }
            int max, itStabilite;
            double prStabilitePct;
            if (!TryParseInt(textBox.Text, out max) || max < 1)
            {
                MessageBox.Show("Taille maximale de l'ES invalide (entier >= 1).", "Erreur");
                return;
            }
            if (!TryParseInt(textBox2.Text, out itStabilite) || itStabilite < 0)
            {
                MessageBox.Show("Nombre d'itérations de stabilité invalide (entier >= 0).", "Erreur");
                return;
            }
            if (!TryParseDouble(textBox3.Text, out prStabilitePct) || prStabilitePct < 0 || prStabilitePct > 100)
            {
                MessageBox.Show("Pourcentage de ressemblance invalide (0 à 100).", "Erreur");
                return;
            }
            double prStabilite = prStabilitePct / 100.0;
            string[] parts = (textBox4.Text ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            double[] poids = new double[parts.Length];
            for (int p = 0; p < parts.Length; p++)
            {
                if (!TryParseDouble(parts[p], out poids[p]))
                {
                    MessageBox.Show("Poids invalides. Exemple : 0,45;0,35;0,20", "Erreur");
                    return;
                }
            }
            if (poids.Length != imagesBmp.Count)
            {
                MessageBox.Show("Il faut autant de poids que de bandes (" + imagesBmp.Count + " bande(s), " + poids.Length + " poids).", "Erreur");
                return;
            }
            int w0 = imagesBmp[0].Width, h0 = imagesBmp[0].Height;
            for (int k = 1; k < imagesBmp.Count; k++)
            {
                if (imagesBmp[k].Width != w0 || imagesBmp[k].Height != h0)
                {
                    MessageBox.Show("Toutes les bandes doivent avoir la même taille.", "Erreur");
                    return;
                }
            }

            string outDir = DossierResultats();
            Directory.CreateDirectory(outDir);

            List<int[,]> imagesMat;
            try
            {
                imagesMat = bmpToMat(imagesBmp);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible de lire les images : " + ex.Message, "Erreur");
                return;
            }

            try
            {

            List<int[,]> enviOuvert = new List<int[,]>();
            List<int[,]> enviFerme = new List<int[,]>();
            List<string> nomsOuvert = new List<string>();
            List<string> nomsFerme = new List<string>();

            for (int i = 1; i <= max; i++)
            {
                List<int[,]> imagesErodeInit = new List<int[,]>();
                List<int[,]> imagesDilateInit = new List<int[,]>();
                List<int[,]> imagesOuvertesStandards = new List<int[,]>();
                List<int[,]> imagesFermeesStandards = new List<int[,]>();

                ErosionDilatationInit(imagesMat, poids, ref imagesErodeInit, ref imagesDilateInit, i);
                OuvertureFermetureStandard(imagesErodeInit, imagesDilateInit, poids, ref imagesOuvertesStandards, ref imagesFermeesStandards, i);

                for (int k = 0; k < imagesBmp.Count; k++)
                {
                    Bitmap sortieErod = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                    Bitmap sortieDilat = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                    Bitmap sortieOuverteStandard = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                    Bitmap sortieFermeeStandard = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                    for (int x = 0; x < imagesBmp[k].Width; x++)
                        for (int y = 0; y < imagesBmp[k].Height; y++)
                        {
                            sortieErod.SetPixel(x, y, Gray(imagesErodeInit[k][x, y]));
                            sortieDilat.SetPixel(x, y, Gray(imagesDilateInit[k][x, y]));
                            sortieOuverteStandard.SetPixel(x, y, Gray(imagesOuvertesStandards[k][x, y]));
                            sortieFermeeStandard.SetPixel(x, y, Gray(imagesFermeesStandards[k][x, y]));
                        }
                    sortieErod.Save(Path.Combine(outDir, "Erod B_" + k + " ES_" + i + ".tiff"));
                    sortieDilat.Save(Path.Combine(outDir, "Dilat B_" + k + " ES_" + i + ".tiff"));
                    sortieOuverteStandard.Save(Path.Combine(outDir, "OuvertureStandard B_" + k + " ES_" + i + ".tiff"));
                    sortieFermeeStandard.Save(Path.Combine(outDir, "FermetureStandard B_" + k + " ES_" + i + ".tiff"));
                    sortieErod.Dispose();
                    sortieDilat.Dispose();
                    sortieOuverteStandard.Dispose();
                    sortieFermeeStandard.Dispose();
                }

                List<int[,]> gNewFerme = new List<int[,]>();
                List<int[,]> gNewOuvert = new List<int[,]>();
                Reconstruction(imagesMat, imagesErodeInit, imagesDilateInit, poids, itStabilite, prStabilite, ref gNewOuvert, ref gNewFerme);

                for (int k = 0; k < imagesBmp.Count; k++)
                {
                    Bitmap sortieFerme = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                    Bitmap sortieOuvert = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                    for (int x = 0; x < imagesBmp[k].Width; x++)
                        for (int y = 0; y < imagesBmp[k].Height; y++)
                        {
                            sortieFerme.SetPixel(x, y, Gray(gNewFerme[k][x, y]));
                            sortieOuvert.SetPixel(x, y, Gray(gNewOuvert[k][x, y]));
                        }
                    sortieFerme.Save(Path.Combine(outDir, "FermeReconstruction B_" + k + " ES_" + i + ".tiff"));
                    sortieOuvert.Save(Path.Combine(outDir, "OuvertReconstruction B_" + k + " ES_" + i + ".tiff"));
                    sortieFerme.Dispose();
                    sortieOuvert.Dispose();
                    enviOuvert.Add((int[,])gNewOuvert[k].Clone());
                    enviFerme.Add((int[,])gNewFerme[k].Clone());
                    nomsOuvert.Add("OuvertReconstruction B_" + k + " ES_" + i);
                    nomsFerme.Add("FermeReconstruction B_" + k + " ES_" + i);
                }
            }

                List<int[,]> enviTout = new List<int[,]>();
                List<string> nomsTout = new List<string>();
                enviTout.AddRange(enviOuvert);
                enviTout.AddRange(enviFerme);
                nomsTout.AddRange(nomsOuvert);
                nomsTout.AddRange(nomsFerme);
                WriteEnviMultiband(outDir, "EXTENDED-PROFIL-MOR-multibande", w0, h0, enviTout, nomsTout);
                WriteEnviMultiband(outDir, "OuvertReconstruction-multibande", w0, h0, enviOuvert, nomsOuvert);
                WriteEnviMultiband(outDir, "FermeReconstruction-multibande", w0, h0, enviFerme, nomsFerme);

                MessageBox.Show("Traitement terminé.\nRésultats (TIFF + ENVI .hdr) dans :\n" + outDir, "EMP_AHP_UniquePreferenceFunction");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur pendant le calcul :\n" + ex.Message, "Erreur");
            }
        }

        private static SDColor Gray(int v)
        {
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return SDColor.FromArgb(v, v, v);
        }

        private static void WriteEnviMultiband(string outDir, string baseName, int width, int height, List<int[,]> bands, List<string> bandNames)
        {
            if (bands == null || bands.Count == 0)
                return;
            string imgPath = Path.Combine(outDir, baseName + ".img");
            string hdrPath = Path.Combine(outDir, baseName + ".hdr");
            using (FileStream fs = new FileStream(imgPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buf = new byte[width * height];
                for (int b = 0; b < bands.Count; b++)
                {
                    int idx = 0;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int v = bands[b][x, y];
                            if (v < 0) v = 0;
                            if (v > 255) v = 255;
                            buf[idx++] = (byte)v;
                        }
                    }
                    fs.Write(buf, 0, buf.Length);
                }
            }
            var names = new System.Text.StringBuilder();
            for (int i = 0; i < bandNames.Count; i++)
            {
                if (i > 0) names.Append(",\r\n ");
                names.Append(bandNames[i]);
            }
            string hdr =
                "ENVI\r\n" +
                "description = { EMP_AHP_UniquePreferenceFunction, " + baseName + " }\r\n" +
                "samples = " + width + "\r\n" +
                "lines = " + height + "\r\n" +
                "bands = " + bands.Count + "\r\n" +
                "header offset = 0\r\n" +
                "file type = ENVI Standard\r\n" +
                "data type = 1\r\n" +
                "interleave = bsq\r\n" +
                "byte order = 0\r\n" +
                "band names = {\r\n " + names + "\r\n}\r\n";
            File.WriteAllText(hdrPath, hdr, System.Text.Encoding.ASCII);
        }

        private List<SDPoint> GetDiskOffsets(int r)
        {
            var offs = new List<SDPoint>();
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                    if (dx * dx + dy * dy <= r * r) offs.Add(new SDPoint(dx, dy));
            return offs;
        }

        private bool IsBorder(int x, int y, int W, int H, List<SDPoint> offs)
        {
            foreach (var p in offs)
            {
                int nx = x + p.X, ny = y + p.Y;
                if (nx < 0 || ny < 0 || nx >= W || ny >= H) return true;
            }
            return false;
        }

        // =====================================================================================
        // [V4-ANTI-CONDORCET] ORDRE DE RÉFÉRENCE FIXE POUR LE ∧ / ∨ POINT À POINT DE LA RECONSTRUCTION
        // =====================================================================================
        // Méthode : AHP.
        // Problème corrigé : jusqu'ici, le ∧ / ∨ point à point de la reconstruction comparait deux vecteurs
        // seuls (la méthode appliquée à l'ensemble {A, B}). Cette comparaison n'est pas transitive : elle
        // admet des cycles de Condorcet (A > B, B > C et C > A), et la reconstruction pouvait tourner en
        // boucle sans jamais se stabiliser.
        // Correction : chaque vecteur v reçoit un score FIXE, calculé avec la même méthode et les mêmes
        // paramètres, mais toujours contre le même panel de référence R : au plus TAILLE_PANEL_REF
        // vecteurs prélevés une fois pour toutes dans l'image d'entrée f.
        //   S_R(v) = priorité globale AHP de v dans l'ensemble R ∪ {v} (mêmes matrices de préférence
        //   1 + d / 1/(1 + |d|), même normalisation par colonne, mêmes poids que MinMaxVecteurs).
        // Deux vecteurs sont comparés par ce score, puis par l'ordre lexicographique des bandes en cas
        // d'égalité : c'est un ordre TOTAL sur les valeurs (réflexif, antisymétrique, transitif), donc
        // aucun cycle de Condorcet n'est possible. Le classement des n pixels de la fenêtre
        // (MinMaxVecteurs) n'est PAS modifié : seul le ∧ / ∨ point à point de la reconstruction change.
        private const int TAILLE_PANEL_REF = 256;          // [V4-ANTI-CONDORCET] nombre maximal de vecteurs du panel R
        private List<int[]> panelRef;                       // [V4-ANTI-CONDORCET] panel de référence R
        private int[] prioriteRef;                          // [V4-ANTI-CONDORCET] ordre des bandes pour le départage
        private Dictionary<int[], double> cacheScoreRef;    // [V4-ANTI-CONDORCET] score déjà calculé de chaque vecteur
        private double[] poidsRef;                          // [V4-ANTI-CONDORCET] poids des bandes
        private double[][] colRef;                          // [V4-ANTI-CONDORCET] sommes par colonne des préférences du panel
        // [V4-ANTI-CONDORCET] Égalité exacte de deux vecteurs (clé du cache des scores)
        private sealed class ComparateurVecteurs : IEqualityComparer<int[]>
        {
            public bool Equals(int[] a, int[] b)
            {
                if (ReferenceEquals(a, b)) return true;
                if (a == null || b == null || a.Length != b.Length) return false;
                for (int i = 0; i < a.Length; i++)
                    if (a[i] != b[i]) return false;
                return true;
            }

            public int GetHashCode(int[] a)
            {
                unchecked
                {
                    int h = 17;
                    for (int i = 0; i < a.Length; i++) h = h * 31 + a[i];
                    return h;
                }
            }
        }

        // [V4-ANTI-CONDORCET] Construction du panel R (prélèvement systématique : un pixel sur 'pas', dans
        // l'ordre de parcours x puis y de l'image d'entrée) et de l'ordre de départage des bandes.
        private void PreparerOrdreReference(List<int[,]> imagesMat, double[] poids)
        {
            int m = imagesMat.Count;
            int W = imagesMat[0].GetLength(0), H = imagesMat[0].GetLength(1);
            int total = W * H;
            int pas = Math.Max(1, (total + TAILLE_PANEL_REF - 1) / TAILLE_PANEL_REF);
            panelRef = new List<int[]>();
            for (int p = 0; p < total; p += pas)
            {
                int[] v = new int[m];
                for (int k = 0; k < m; k++) v[k] = imagesMat[k][p / H, p % H];
                panelRef.Add(v);
            }
            // Départage : bandes par poids décroissant (à poids égaux, la bande d'indice le plus petit d'abord)
            prioriteRef = new int[m];
            for (int k = 0; k < m; k++) prioriteRef[k] = k;
            for (int a = 1; a < m; a++)   // tri par insertion (stable)
            {
                int bande = prioriteRef[a];
                int j = a - 1;
                while (j >= 0 && poids[prioriteRef[j]] < poids[bande])
                {
                    prioriteRef[j + 1] = prioriteRef[j];
                    j--;
                }
                prioriteRef[j + 1] = bande;
            }
            poidsRef = (double[])poids.Clone();
            // Sommes par colonne des matrices de préférence AHP restreintes au panel :
            // colRef[k][j] = somme_{l dans R} P_k(r_l, r_j)
            int nR = panelRef.Count;
            colRef = new double[m][];
            for (int k = 0; k < m; k++)
            {
                colRef[k] = new double[nR];
                for (int j = 0; j < nR; j++)
                {
                    double somme = 0;
                    for (int l = 0; l < nR; l++) somme += PreferenceAHP(panelRef[l][k], panelRef[j][k]);
                    colRef[k][j] = somme;
                }
            }
            cacheScoreRef = new Dictionary<int[], double>(new ComparateurVecteurs());
        }

        // [V4-ANTI-CONDORCET] Préférence AHP de la valeur gi sur la valeur gj (même formule que MinMaxVecteurs)
        private static double PreferenceAHP(int gi, int gj)
        {
            int diff = gi - gj;
            return diff >= 0 ? 1 + diff : 1.0 / (1 - diff);
        }

        // [V4-ANTI-CONDORCET] Score fixe S_R(v) : priorité globale AHP de v dans l'ensemble R ∪ {v}
        private double ScoreReference(int[] v)
        {
            double s;
            if (cacheScoreRef.TryGetValue(v, out s)) return s;
            int m = v.Length;
            int nR = panelRef.Count;
            double total = 0;
            for (int k = 0; k < m; k++)
            {
                double ligne = 0;        // somme de la ligne de v dans la matrice normalisée par colonne
                double colonneV = 1.0;   // somme de la colonne de v : P(v, v) = 1 + somme_j P(r_j, v)
                for (int j = 0; j < nR; j++)
                {
                    double pvj = PreferenceAHP(v[k], panelRef[j][k]);
                    ligne += pvj / (colRef[k][j] + pvj);                // colonne j = panel + ligne de v
                    colonneV += PreferenceAHP(panelRef[j][k], v[k]);
                }
                ligne += 1.0 / colonneV;                                  // terme P(v, v) / somme de la colonne de v
                total += poidsRef[k] * ligne / (nR + 1);                  // w_k * alpha_v^(k)
            }
            s = total;
            if (double.IsNaN(s) || double.IsInfinity(s)) s = 0;   // sécurité : le score reste une fonction de v
            s = Math.Round(s, 10);                                 // arrondi fixe : ex aequo numériques => départage lexicographique
            cacheScoreRef[v] = s;
            return s;
        }

        // [V4-ANTI-CONDORCET] Comparaison de deux vecteurs pour le ∧ / ∨ point à point de la reconstruction :
        // score fixe (contre le panel R), puis ordre lexicographique des bandes (prioriteRef) en cas d'égalité.
        // C'est un ordre total sur les valeurs : il est transitif, aucun cycle de Condorcet n'est possible.
        // Retourne -1 si A < B, 0 si A et B sont le même vecteur, 1 si A > B.
        private int CompareReference(List<int[,]> imgA, int xa, int ya, List<int[,]> imgB, int xb, int yb)
        {
            int m = imgA.Count;
            int[] a = new int[m];
            int[] b = new int[m];
            for (int k = 0; k < m; k++)
            {
                a[k] = imgA[k][xa, ya];
                b[k] = imgB[k][xb, yb];
            }
            double sa = ScoreReference(a);
            double sb = ScoreReference(b);
            if (sa < sb) return -1;
            if (sa > sb) return 1;
            foreach (int k in prioriteRef)
            {
                if (a[k] < b[k]) return -1;
                if (a[k] > b[k]) return 1;
            }
            return 0;
        }


        private void Reconstruction(List<int[,]> imagesMat, List<int[,]> imagesErodeInit, List<int[,]> imagesDilateInit, double[] poids, int itStabilite, double prStabilite, ref List<int[,]> gNewOuvert, ref List<int[,]> gNewFerme)
        {
            List<int[,]> gLastFerme = new List<int[,]>();
            List<int[,]> gLastOuvert = new List<int[,]>();
            for (int k = 0; k < imagesMat.Count; k++)
            {
                gNewFerme.Add(new int[imagesMat[0].GetLength(0), imagesMat[0].GetLength(1)]);
                gNewOuvert.Add(new int[imagesMat[0].GetLength(0), imagesMat[0].GetLength(1)]);
                gLastOuvert.Add((int[,])imagesErodeInit[k].Clone());
                gLastFerme.Add((int[,])imagesDilateInit[k].Clone());
            }

            // [V4-ANTI-CONDORCET] Ordre de référence fixe (transitif) utilisé par le ∧ / ∨ point à point.
            PreparerOrdreReference(imagesMat, poids);
            // [V4-ANTI-CONDORCET] Marqueurs placés du bon côté du masque f (au sens de l'ordre de référence) :
            //   ouverture : h0 = inf(epsilon(f), f) ;  fermeture : h0 = sup(delta(f), f).
            // Avec la règle de monotonie de ErosionDilatationGeodesique, la suite des marqueurs est monotone
            // et bornée par f dans un ensemble fini de vecteurs : la stabilité est TOUJOURS atteinte.
            for (int x = 0; x < imagesMat[0].GetLength(0); x++)
                for (int y = 0; y < imagesMat[0].GetLength(1); y++)
                {
                    if (CompareReference(gLastOuvert, x, y, imagesMat, x, y) > 0)
                        for (int k = 0; k < imagesMat.Count; k++) gLastOuvert[k][x, y] = imagesMat[k][x, y];
                    if (CompareReference(gLastFerme, x, y, imagesMat, x, y) < 0)
                        for (int k = 0; k < imagesMat.Count; k++) gLastFerme[k][x, y] = imagesMat[k][x, y];
                }
            int toleranceStabilite = 0;
            if (itStabilite == 0) itStabilite = int.MaxValue;
            bool stopErod = false, stopDilat = false;
            while (((!stopDilat) || (!stopErod)) && (toleranceStabilite < itStabilite))
            {
                stopErod = true; stopDilat = true;
                ErosionDilatationGeodesique(imagesMat, gLastFerme, gLastOuvert, poids, prStabilite, ref gNewFerme, ref gNewOuvert, ref stopErod, ref stopDilat);
                for (int k = 0; k < imagesMat.Count; k++)
                {
                    gLastOuvert[k] = (int[,])gNewOuvert[k].Clone();
                    gLastFerme[k] = (int[,])gNewFerme[k].Clone();
                }
                toleranceStabilite++;
            }
        }

        // [V4-ANTI-CONDORCET] Le ∧ / ∨ point à point utilise CompareReference (ordre de référence fixe et transitif)
        // et la suite des marqueurs est monotone (voir la section ORDRE DE RÉFÉRENCE FIXE).
        private void ErosionDilatationGeodesique(List<int[,]> imagesMat, List<int[,]> gLastFerme, List<int[,]> gLastOuvert, double[] poids, double prStabilite, ref List<int[,]> gNewFerme, ref List<int[,]> gNewOuvert, ref bool stopErod, ref bool stopDilat)
        {
            int W = imagesMat[0].GetLength(0), H = imagesMat[0].GetLength(1);
            var offsB1 = GetDiskOffsets(1);
            int ressemblanceOuverture = 0, ressemblanceFermeture = 0;
            for (int x = 0; x < W; x++)
            {
                for (int y = 0; y < H; y++)
                {
                    if (IsBorder(x, y, W, H, offsB1))
                    {
                        for (int k = 0; k < imagesMat.Count; k++)
                        {
                            gNewFerme[k][x, y] = gLastFerme[k][x, y];
                            gNewOuvert[k][x, y] = gLastOuvert[k][x, y];
                        }
                    }
                    else
                    {
                        int sMax = 0, tMax = 0, sMin = 0, tMin = 0;
                        MinMaxVecteurs(gLastFerme, gLastOuvert, poids, x, y, ref sMin, ref tMin, ref sMax, ref tMax, 1);
                        // [V4-ANTI-CONDORCET] sup(epsilon_B1(h), f) au sens de l'ordre de référence fixe (transitif)
                        int cmpFerme = CompareReference(imagesMat, x, y, gLastFerme, sMin, tMin);
                        if (cmpFerme < 0)
                            for (int k = 0; k < imagesMat.Count; k++) gNewFerme[k][x, y] = gLastFerme[k][sMin, tMin];
                        else
                            for (int k = 0; k < imagesMat.Count; k++) gNewFerme[k][x, y] = imagesMat[k][x, y];
                        // [V4-ANTI-CONDORCET] Monotonie : la fermeture par reconstruction est la limite d'une suite
                        // DÉCROISSANTE. Si la nouvelle valeur est plus grande que la précédente (au sens de l'ordre
                        // de référence), la valeur précédente est conservée : aucun aller-retour, donc aucun cycle.
                        if (CompareReference(gNewFerme, x, y, gLastFerme, x, y) > 0)
                            for (int k = 0; k < imagesMat.Count; k++) gNewFerme[k][x, y] = gLastFerme[k][x, y];
                        // [V4-ANTI-CONDORCET] inf(delta_B1(h), f) au sens de l'ordre de référence fixe (transitif)
                        int cmpOuvert = CompareReference(gLastOuvert, sMax, tMax, imagesMat, x, y);
                        if (cmpOuvert < 0)
                            for (int k = 0; k < imagesMat.Count; k++) gNewOuvert[k][x, y] = gLastOuvert[k][sMax, tMax];
                        else
                            for (int k = 0; k < imagesMat.Count; k++) gNewOuvert[k][x, y] = imagesMat[k][x, y];
                        // [V4-ANTI-CONDORCET] Monotonie : l'ouverture par reconstruction est la limite d'une suite
                        // CROISSANTE. Si la nouvelle valeur est plus petite que la précédente (au sens de l'ordre
                        // de référence), la valeur précédente est conservée : aucun aller-retour, donc aucun cycle.
                        if (CompareReference(gNewOuvert, x, y, gLastOuvert, x, y) < 0)
                            for (int k = 0; k < imagesMat.Count; k++) gNewOuvert[k][x, y] = gLastOuvert[k][x, y];
                        int locOuv = 0, locFerm = 0;
                        for (int k = 0; k < imagesMat.Count; k++)
                        {
                            if (gNewFerme[k][x, y] != gLastFerme[k][x, y]) stopErod = false; else locFerm++;
                            if (gNewOuvert[k][x, y] != gLastOuvert[k][x, y]) stopDilat = false; else locOuv++;
                        }
                        if (locOuv == imagesMat.Count) ressemblanceOuverture++;
                        if (locFerm == imagesMat.Count) ressemblanceFermeture++;
                    }
                }
            }
            double tauxOuv = (double)ressemblanceOuverture / (W * H);
            double tauxFerm = (double)ressemblanceFermeture / (W * H);
            if (tauxOuv >= prStabilite) stopDilat = true;
            if (tauxFerm >= prStabilite) stopErod = true;
        }

        // [V4-ANTI-CONDORCET] Ancienne comparaison (versions précédentes), conservée pour mémoire : la reconstruction
        // ne l'appelle plus, elle utilise CompareReference (ordre de référence fixe et transitif).
        private int CompareDeuxVecteursAHP(List<int[,]> imgA, int xa, int ya, List<int[,]> imgB, int xb, int yb, double[] poids)
        {
            int m = imgA.Count;
            double sA = 0, sB = 0;
            for (int k = 0; k < m; k++)
            {
                int va = imgA[k][xa, ya];
                int vb = imgB[k][xb, yb];
                double pAB, pBA;
                if (va >= vb) { pAB = 1 + (va - vb); pBA = 1.0 / (1 + (va - vb)); }
                else { pAB = 1.0 / (1 + (vb - va)); pBA = 1 + (vb - va); }
                double sumColA = 1 + pBA;
                double sumColB = pAB + 1;
                double alphaA = 0.5 * (1.0 / sumColA + pAB / sumColB);
                double alphaB = 0.5 * (pBA / sumColA + 1.0 / sumColB);
                sA += alphaA * poids[k];
                sB += alphaB * poids[k];
            }
            const double eps = 1e-12;
            if (Math.Abs(sA - sB) > eps) return sA < sB ? -1 : 1;
            for (int k = 0; k < m; k++)
            {
                int va = imgA[k][xa, ya];
                int vb = imgB[k][xb, yb];
                if (va < vb) return -1;
                if (va > vb) return 1;
            }
            return 0;
        }

        // Ouverture = dilatation de l'érodé ; fermeture = érosion du dilaté (V3) : même ES
        //   (disque euclidien de rayon 'rayon'), appliqué en une seule passe à chaque opération.
        //   Pixel de bord (disque débordant de l'image) : valeur de l'image d'entrée conservée.
        private void OuvertureFermetureStandard(List<int[,]> imagesErodeInit, List<int[,]> imagesDilateInit, double[] poids, ref List<int[,]> imagesOuvertesStandards, ref List<int[,]> imagesFermeesStandards, int rayon)
        {
            List<int[,]> imagesErodPrec = new List<int[,]>();
            List<int[,]> imagesDilatePrec = new List<int[,]>();
            for (int k = 0; k < imagesErodeInit.Count; k++)
            {
                imagesOuvertesStandards.Add(new int[imagesErodeInit[0].GetLength(0), imagesErodeInit[0].GetLength(1)]);
                imagesFermeesStandards.Add(new int[imagesErodeInit[0].GetLength(0), imagesErodeInit[0].GetLength(1)]);
                imagesErodPrec.Add((int[,])imagesDilateInit[k].Clone());
                imagesDilatePrec.Add((int[,])imagesErodeInit[k].Clone());
            }
            // V3 : ES = disque euclidien de rayon 'rayon' (pixels tels que dx² + dy² <= rayon²),
            // utilisé directement en une seule passe : tous les pixels du disque sont comparés ensemble.
            var offsBi = GetDiskOffsets(rayon);
            int W = imagesErodeInit[0].GetLength(0), H = imagesErodeInit[0].GetLength(1);
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    // Pixel de bord (le disque de rayon 'rayon' déborde de l'image) : valeur d'entrée conservée
                    if (IsBorder(x, y, W, H, offsBi))
                    {
                        for (int k = 0; k < imagesErodeInit.Count; k++)
                        {
                            imagesOuvertesStandards[k][x, y] = imagesDilatePrec[k][x, y];
                            imagesFermeesStandards[k][x, y] = imagesErodPrec[k][x, y];
                        }
                    }
                    else
                    {
                        int sMax = 0, tMax = 0, sMin = 0, tMin = 0;
                        MinMaxVecteurs(imagesErodPrec, imagesDilatePrec, poids, x, y, ref sMin, ref tMin, ref sMax, ref tMax, rayon);
                        for (int k = 0; k < imagesErodeInit.Count; k++)
                        {
                            imagesFermeesStandards[k][x, y] = imagesErodPrec[k][sMin, tMin];
                            imagesOuvertesStandards[k][x, y] = imagesDilatePrec[k][sMax, tMax];
                        }
                    }
                }
        }

        // Érosion (infimum) et dilatation (supremum) multivaluées de taille 'rayon' (V3) :
        //   ES = disque euclidien de rayon 'rayon' ; pour chaque pixel, l'infimum et le supremum sont
        //   cherchés en UNE SEULE PASSE parmi TOUS les pixels-vecteurs couverts par ce disque.
        //   Pixel de bord (disque débordant de l'image) : valeur de l'image d'entrée conservée.
        private void ErosionDilatationInit(List<int[,]> imagesMat, double[] poids, ref List<int[,]> imagesErodeInit, ref List<int[,]> imagesDilateInit, int rayon)
        {
            List<int[,]> imagesErodPrec = new List<int[,]>();
            List<int[,]> imagesDilatePrec = new List<int[,]>();
            for (int k = 0; k < imagesMat.Count; k++)
            {
                imagesDilateInit.Add(new int[imagesMat[0].GetLength(0), imagesMat[0].GetLength(1)]);
                imagesErodeInit.Add(new int[imagesMat[0].GetLength(0), imagesMat[0].GetLength(1)]);
                imagesErodPrec.Add((int[,])imagesMat[k].Clone());
                imagesDilatePrec.Add((int[,])imagesMat[k].Clone());
            }
            // V3 : ES = disque euclidien de rayon 'rayon' (pixels tels que dx² + dy² <= rayon²),
            // utilisé directement en une seule passe : tous les pixels du disque sont comparés ensemble.
            var offsBi = GetDiskOffsets(rayon);
            int W = imagesMat[0].GetLength(0), H = imagesMat[0].GetLength(1);
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    // Pixel de bord (le disque de rayon 'rayon' déborde de l'image) : valeur d'entrée conservée
                    if (IsBorder(x, y, W, H, offsBi))
                    {
                        for (int k = 0; k < imagesMat.Count; k++)
                        {
                            imagesDilateInit[k][x, y] = imagesDilatePrec[k][x, y];
                            imagesErodeInit[k][x, y] = imagesErodPrec[k][x, y];
                        }
                    }
                    else
                    {
                        int sMax = 0, tMax = 0, sMin = 0, tMin = 0;
                        MinMaxVecteurs(imagesErodPrec, imagesDilatePrec, poids, x, y, ref sMin, ref tMin, ref sMax, ref tMax, rayon);
                        for (int k = 0; k < imagesMat.Count; k++)
                        {
                            imagesErodeInit[k][x, y] = imagesErodPrec[k][sMin, tMin];
                            imagesDilateInit[k][x, y] = imagesDilatePrec[k][sMax, tMax];
                        }
                    }
                }
        }

        private void MinMaxVecteurs(List<int[,]> imagesErodPrec, List<int[,]> imagesDilatePrec, double[] poids, int x, int y, ref int sMin, ref int tMin, ref int sMax, ref int tMax, int rayonDisque)
        {
            var offs = GetDiskOffsets(rayonDisque);
            int n = offs.Count;
            int m = imagesErodPrec.Count;
            double[][,] prefErod = new double[m][,];
            double[][,] prefDilat = new double[m][,];
            double[][] sumColErod = new double[m][];
            double[][] sumColDilat = new double[m][];
            for (int k = 0; k < m; k++)
            {
                prefErod[k] = new double[n, n];
                prefDilat[k] = new double[n, n];
                sumColErod[k] = new double[n];
                sumColDilat[k] = new double[n];
                for (int j = 0; j < n; j++)
                {
                    int xj = x + offs[j].X, yj = y + offs[j].Y;
                    for (int i = 0; i < n; i++)
                    {
                        int xi = x + offs[i].X, yi = y + offs[i].Y;
                        int diffE = imagesErodPrec[k][xi, yi] - imagesErodPrec[k][xj, yj];
                        prefErod[k][i, j] = diffE >= 0 ? 1 + diffE : 1.0 / (1 - diffE);
                        int diffD = imagesDilatePrec[k][xi, yi] - imagesDilatePrec[k][xj, yj];
                        prefDilat[k][i, j] = diffD >= 0 ? 1 + diffD : 1.0 / (1 - diffD);
                    }
                }
                for (int j = 0; j < n; j++)
                {
                    double sE = 0, sD = 0;
                    for (int l = 0; l < n; l++) { sE += prefErod[k][l, j]; sD += prefDilat[k][l, j]; }
                    sumColErod[k][j] = sE;
                    sumColDilat[k][j] = sD;
                }
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                    {
                        prefErod[k][i, j] /= sumColErod[k][j];
                        prefDilat[k][i, j] /= sumColDilat[k][j];
                    }
            }
            double[,] alphaErod = new double[n, m];
            double[,] alphaDilat = new double[n, m];
            for (int k = 0; k < m; k++)
                for (int i = 0; i < n; i++)
                {
                    double sumE = 0, sumD = 0;
                    for (int j = 0; j < n; j++) { sumE += prefErod[k][i, j]; sumD += prefDilat[k][i, j]; }
                    alphaErod[i, k] = sumE / n;
                    alphaDilat[i, k] = sumD / n;
                }
            double[] sErod = new double[n];
            double[] sDilat = new double[n];
            for (int i = 0; i < n; i++)
                for (int k = 0; k < m; k++)
                {
                    sErod[i] += alphaErod[i, k] * poids[k];
                    sDilat[i] += alphaDilat[i, k] * poids[k];
                }
            int idxMin = 0, idxMax = 0;
            double minVal = sErod[0], maxVal = sDilat[0];
            for (int z = 1; z < n; z++)
            {
                if (sErod[z] < minVal - 1e-12) { minVal = sErod[z]; idxMin = z; }
                else if (Math.Abs(sErod[z] - minVal) < 1e-12)
                {
                    int cx = x + offs[z].X, cy = y + offs[z].Y;
                    int bx = x + offs[idxMin].X, by = y + offs[idxMin].Y;
                    for (int k = 0; k < m; k++)
                    {
                        if (imagesErodPrec[k][cx, cy] < imagesErodPrec[k][bx, by]) { idxMin = z; break; }
                        if (imagesErodPrec[k][cx, cy] > imagesErodPrec[k][bx, by]) break;
                    }
                }
                if (sDilat[z] > maxVal + 1e-12) { maxVal = sDilat[z]; idxMax = z; }
                else if (Math.Abs(sDilat[z] - maxVal) < 1e-12)
                {
                    int cx = x + offs[z].X, cy = y + offs[z].Y;
                    int bx = x + offs[idxMax].X, by = y + offs[idxMax].Y;
                    for (int k = 0; k < m; k++)
                    {
                        if (imagesDilatePrec[k][cx, cy] > imagesDilatePrec[k][bx, by]) { idxMax = z; break; }
                        if (imagesDilatePrec[k][cx, cy] < imagesDilatePrec[k][bx, by]) break;
                    }
                }
            }
            sMin = x + offs[idxMin].X; tMin = y + offs[idxMin].Y;
            sMax = x + offs[idxMax].X; tMax = y + offs[idxMax].Y;
        }

        private List<int[,]> bmpToMat(List<Bitmap> imagesBmp)
        {
            List<int[,]> imagesMat = new List<int[,]>();
            for (int z = 0; z < imagesBmp.Count; z++)
            {
                imagesMat.Add(new int[imagesBmp[z].Width, imagesBmp[z].Height]);
                for (int x = 0; x < imagesBmp[z].Width; x++)
                    for (int y = 0; y < imagesBmp[z].Height; y++)
                        imagesMat[z][x, y] = imagesBmp[z].GetPixel(x, y).R;
            }
            return imagesMat;
        }
    }
}