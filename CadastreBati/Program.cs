using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using GeoAPI.Geometries;
using System.IO;
using System.Net;
using System.IO.Compression;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using System.Configuration;

namespace CadastreBati
{
    public class Commune
    {
        public string nom;
        public string code;
        public string codeDepartement;
    }


    class Program
    {
        static string progress = string.Empty;

        public enum BatimentType { Inconnu = 0, Dur = 1, Leger = 2}

        public static string HttpGet(string uri)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        static List<Commune> GetCodeCommunes(string codePostal)
        {
            Console.WriteLine($"Recherche des communes pour {codePostal} sur geo.api.gouv.fr...");
            string json = HttpGet($"https://geo.api.gouv.fr/communes?codePostal={codePostal}&fields=nom,code,codeDepartement&format=json&geometry=centre");
            if (json != null)
            {
                List<Commune> communes = JsonConvert.DeserializeObject<List<Commune>>(json);
                return communes;
            }
            return null;
        }

        static Commune CheckCommune(string codeCommune)
        {
            string json = HttpGet($"https://geo.api.gouv.fr/communes?code={codeCommune}&fields=nom,code,codeDepartement&format=json&geometry=centre");
            if (json != null)
            {
                List<Commune> communes = JsonConvert.DeserializeObject<List<Commune>>(json);
                if (communes != null && communes.Count > 0)
                {
                    return communes[0];
                }
            }
            return null;
        }

        static string DecompressAndGetText(string filepath)
        {
            FileInfo fileToDecompress = new FileInfo(filepath);
            string fileText;
            using (FileStream originalFileStream = fileToDecompress.OpenRead())
            {
                using (MemoryStream decompressedFileStream = new MemoryStream())
                {
                    using (GZipStream decompressionStream = new GZipStream(originalFileStream, CompressionMode.Decompress))
                    {
                        decompressionStream.CopyTo(decompressedFileStream);

                        fileText = Encoding.UTF8.GetString(decompressedFileStream.ToArray());
                    }
                }
            }
            return fileText;
        }

        static void DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            progress = e.ProgressPercentage.ToString();
        }

        static bool dlIsDone = false;

        private static void DownloadFileCompleted(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
        {
            dlIsDone = true;
        }

        static string jsonFilesUrl = "https://cadastre.data.gouv.fr/data/etalab-cadastre/latest/geojson/communes/";
        static string separateur = ";";

        static void Main(string[] args)
        {
            jsonFilesUrl = ConfigurationManager.AppSettings.Get("jsonFilesUrl");
            separateur = ConfigurationManager.AppSettings.Get("defaultCSVSeparator");

            string consoleInput;
            Commune commune = null;
            bool fromLocal;

            start:
            Console.Clear();
            Console.WriteLine("-------------------------------------------------------------------------------------------");
            Console.WriteLine("                                       CADASTRE BATI");
            Console.WriteLine("-------------------------------------------------------------------------------------------");
            Console.WriteLine("Cet utilitaire télécharge les données du cadastre pour une commune et croise le fichier");
            Console.WriteLine("parcelles et le fichier batiments pour générer la surface, le nombre et le type des");
            Console.WriteLine("batiments sur chaque parcelle, puis crée un fichier *.csv avec les résultats.");
            Console.WriteLine("Les données publiques du cadastre sont téléchargées au format GeoJSON depuis l'adresse");
            Console.WriteLine(jsonFilesUrl);
            Console.WriteLine("-------------------------------------------------------------------------------------------");
            Console.WriteLine("[1] pour télécharger le cadastre avec un code commune INSEE");
            Console.WriteLine("[2] pour télécharger le cadastre avec un code postal");
            Console.WriteLine("[3] pour utiliser des fichiers `*batiments.json` et `*parcelles.json` locaux");
            Console.WriteLine($"[4] pour changer le caractère séparateur du fichier *.csv (caractère courant : '{separateur}')");
            consoleInput = Console.ReadKey().KeyChar.ToString();
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.WriteLine("-------------------------------------------------------------------------------------------");

            if (consoleInput.Contains("1"))
            {
                fromLocal = false;
                Console.WriteLine("Saisir un code commune INSEE:");
                consoleInput = Console.ReadLine();
                commune = CheckCommune(consoleInput);
                if (commune == null)
                {
                    Console.WriteLine($"Commune non trouvée avec geo.api.gouv.fr");
                    Console.WriteLine($"Voulez vous tout de meme poursuivre avec le code {consoleInput} ?");
                    Console.WriteLine($"[O] / [N]");
                    string codeCommuneStr = consoleInput;
                    if (Console.ReadKey().KeyChar.ToString().ToLower() == "o")
                    {
                        Console.SetCursorPosition(0, Console.CursorTop);
                        commune = new Commune()
                        {
                            nom = string.Empty,
                            code = codeCommuneStr,
                            codeDepartement = codeCommuneStr.Substring(0, 2)
                        };
                    }
                    else
                    {
                        goto start;
                    }
                }
                else
                {
                    Console.WriteLine("Commune trouvée avec geo.api.gouv.fr :");
                    Console.WriteLine($"{commune.nom} - Code département : {commune.codeDepartement}");
                }
            }
            else if (consoleInput.Contains("2"))
            {
                fromLocal = false;
                Console.WriteLine("Saisir un code postal:");
                string codePostal = Console.ReadLine();
                List<Commune> communes = GetCodeCommunes(codePostal);
                if (communes == null)
                {
                    Console.WriteLine($"Erreur en essayant de contacter geo.api.gouv.fr, appuyez sur entrée pour revenir au menu.");
                    Console.ReadLine();
                    goto start;
                }

                if (communes.Count == 0)
                {
                    Console.WriteLine($"Aucune commune trouvée, appuyez sur entrée pour revenir au menu.");
                    Console.ReadLine();
                    goto start;
                }

                Console.WriteLine($"{communes.Count} commune(s) trouvée(s) :");
                for (int i = 0; i < communes.Count; i++)
                {
                    Console.WriteLine($"[{i}] pour {communes[i].nom} (code: {communes[i].code})");
                }
                Console.WriteLine($"[A] pour annuler");

                consoleInput = Console.ReadKey().KeyChar.ToString();
                Console.SetCursorPosition(0, Console.CursorTop);

                if (int.TryParse(consoleInput, out int j) && j >= 0 && j < communes.Count)
                {
                    commune = communes[j];
                }
                else
                {
                    goto start;
                }
            }
            else if (consoleInput.Contains("3"))
            {
                fromLocal = true;
            }
            else if (consoleInput.Contains("4"))
            {
                Console.WriteLine("[1] pour utiliser le caractère `;` (recommendé pour excel)");
                Console.WriteLine("[2] pour utiliser des tabulations (recommendé pour google docs)");
                Console.WriteLine("Ou entrez le caractère de votre choix");
                consoleInput = Console.ReadLine();
                switch (consoleInput)
                {
                    case "1": 
                        separateur = ";"; break;
                    case "2":
                        separateur = "\t"; break;
                    default:
                        separateur = consoleInput; break;
                }
                goto start;
            }
            else
            {
                goto start;
            }

            Console.WriteLine("-------------------------------------------------------------------------------------------");

            string batimentsStr = null;
            string parcellesStr = null;
            string fileName;

            if (!fromLocal)
            {
                string baseUri = $"{jsonFilesUrl}{commune.codeDepartement}/{commune.code}/";
                string parcellesUri = $"{baseUri}cadastre-{commune.code}-parcelles.json.gz";
                string parcellesPath = Path.Combine(Path.GetTempPath(), $"{commune.code}-parcelles.json.gz");
                string batimentsUri = $"{baseUri}cadastre-{commune.code}-batiments.json.gz";
                string batimentsPath = Path.Combine(Path.GetTempPath(), $"{commune.code}-batiments.json.gz");

                if (commune.nom.Length > 0)
                {
                    fileName = $"{string.Join("_", commune.nom.Split(Path.GetInvalidFileNameChars()))}_{commune.code}_cadastre.csv";
                }
                else
                {
                    fileName = $"{commune.code}_cadastre.csv";
                }

                if (File.Exists(parcellesPath))
                    File.Delete(parcellesPath);

                using (WebClient wc = new WebClient())
                {
                    Console.WriteLine($"Téléchargement des données cadastre (parcelles)... ");
                    Console.WriteLine($"_");
                    dlIsDone = false;
                    wc.DownloadProgressChanged += DownloadProgressChanged;
                    wc.DownloadFileCompleted += DownloadFileCompleted;
                    wc.DownloadFileAsync(new Uri(parcellesUri), parcellesPath);
                    while (!dlIsDone)
                    {
                        System.Threading.Thread.Sleep(50);
                        Console.SetCursorPosition(0, Console.CursorTop - 1);
                        Console.WriteLine($"Telechargement : {progress}%    ");
                    }

                    System.Threading.Thread.Sleep(500);
                }


                if (File.Exists(batimentsPath))
                    File.Delete(batimentsPath);

                using (WebClient wc = new WebClient())
                {
                    Console.WriteLine($"Téléchargement des données cadastre (batiments)... ");
                    Console.WriteLine($"_");
                    dlIsDone = false;
                    wc.DownloadProgressChanged += DownloadProgressChanged;
                    wc.DownloadFileCompleted += DownloadFileCompleted;
                    wc.DownloadFileAsync(new Uri(batimentsUri), batimentsPath);
                    while (!dlIsDone)
                    {
                        System.Threading.Thread.Sleep(50);
                        Console.SetCursorPosition(0, Console.CursorTop - 1);
                        Console.WriteLine($"Telechargement : {progress}%    ");
                    }
                    System.Threading.Thread.Sleep(500);
                }

                parcellesStr = DecompressAndGetText(parcellesPath);
                if (string.IsNullOrEmpty(parcellesStr))
                {
                    Console.WriteLine($"Erreur de téléchargement pour le fichier parcelles\n{parcellesUri}");
                    Console.WriteLine($"Enter pour recommencer");
                    Console.ReadKey();
                    goto start;
                }

                batimentsStr = DecompressAndGetText(batimentsPath);
                if (string.IsNullOrEmpty(batimentsStr))
                {
                    Console.WriteLine($"Erreur de téléchargement pour le fichier batiments\n{batimentsUri}");
                    Console.WriteLine($"Enter pour recommencer");
                    Console.ReadKey();
                    goto start;
                }
            }
            else
            {
                tryagain:

                Console.WriteLine($"Entrez le chemin vers le fichier *batiments.json :");
                try
                {
                    batimentsStr = File.ReadAllText(Console.ReadLine());
                }
                catch (Exception) { }

                if (string.IsNullOrEmpty(batimentsStr))
                {
                    Console.WriteLine($"Erreur a l'ouverture du fichier");
                    goto tryagain;
                }
                Console.WriteLine($"Entrez le chemin vers le fichier *parcelles.json :");
                try
                {
                    parcellesStr = File.ReadAllText(Console.ReadLine());
                }
                catch (Exception) { }

                if (string.IsNullOrEmpty(parcellesStr))
                {
                    Console.WriteLine($"Erreur a l'ouverture du fichier");
                    goto tryagain;
                }

                fileName = "cadastre.csv";
            }

            Console.WriteLine($"Calcul des surfaces baties sur chaque parcelle :");
            Console.WriteLine($"Preparation...");

            var reader = new NetTopologySuite.IO.GeoJsonReader();

            FeatureCollection batiments = reader.Read<FeatureCollection>(batimentsStr);
            FeatureCollection parcelles = reader.Read<FeatureCollection>(parcellesStr);

            Console.SetCursorPosition(0, Console.CursorTop - 1);
            Console.WriteLine($"{batiments.Features.Count} batiments trouvés.");
            Console.WriteLine($"Preparation...");

            List<Polygon> topoBatiments = new List<Polygon>(batiments.Features.Count * 3);
            List<BatimentType> topoBatimentTypes = new List<BatimentType>(batiments.Features.Count * 3);

            int erreurs = 0;

            foreach (Feature feature in batiments.Features)
            {
                MultiPolygon multiPolygon = (MultiPolygon)feature.Geometry;

                foreach (Polygon poly in multiPolygon.Geometries)
                {
                    topoBatiments.Add(poly);

                    object type = feature.Attributes["type"];
                    if (type != null && int.TryParse((string)type, out int valInt))
                    {
                        topoBatimentTypes.Add((BatimentType)valInt);
                    }
                    else
                    {
                        topoBatimentTypes.Add(BatimentType.Inconnu);
                    }
                }
            }

            int parcelleCount = parcelles.Features.Count;
            string[] parcellesSurface = new string[parcelleCount + 1];

            parcellesSurface[0] =
                "section" + separateur
                + "parcelle" + separateur
                + "latitude" + separateur
                + "longitude" + separateur
                + "surface" + separateur
                + "nb bati" + separateur
                + "surface batie" + separateur
                + "nb bati dur" + separateur
                + "surface batie dur" + separateur
                + "nb bati leger" + separateur
                + "surface bati leger";


            int parcellesBatiesCount = 0;
            double surfaceBatieTotale = 0.0;
            double surfaceParcelles = 0.0;

            for (int i = 0; i < parcelleCount; i++)
            {
                Feature feature = (Feature)parcelles.Features[i];

                Polygon[] polygons;
                if (feature.Geometry is MultiPolygon multiPoly)
                {
                    polygons = new Polygon[multiPoly.Count];
                    for (int j = 0; j < multiPoly.Count; j++)
                    {
                        polygons[j] = (Polygon)multiPoly[j];
                    }
                }
                else
                {
                    polygons = new Polygon[] { (Polygon)feature.Geometry };
                }

                Coordinate centroid = feature.Geometry.Centroid.Coordinate;

                double surfaceBatie = 0.0;
                int batimentCount = 0;

                double surfaceBatieDur = 0.0;
                int batimentDurCount = 0;

                double surfaceBatieLeger = 0.0;
                int batimentlegerCount = 0;

                foreach (Polygon polygon in polygons)
                {
                    ILineString line = polygon.ExteriorRing;

                    for (int t = 0; t < topoBatiments.Count; t++)
                    {
                        if (!polygon.Intersects(topoBatiments[t]))
                            continue;

                        IGeometry intersection;
                        try
                        {
                            intersection = polygon.Intersection(topoBatiments[t]);
                        }
                        catch (Exception)
                        {
                            erreurs++;
                            continue;
                        }
                        

                        if (intersection.Area == 0.0)
                            continue;

                        double area = 0.0;
                        if (intersection.NumGeometries > 1)
                        {
                            foreach (IGeometry subGeometry in (IGeometryCollection)intersection)
                            {
                                if (subGeometry.Area > 0.0)
                                {
                                    area += SphericalUtil.ComputeSignedArea(subGeometry.Coordinates);
                                }
                            }
                        }
                        else
                        {
                            area += Math.Abs(SphericalUtil.ComputeSignedArea(intersection.Coordinates));
                        }

                        if (area > 1.0)
                        {
                            batimentCount++;
                            surfaceBatie += area;
                            switch (topoBatimentTypes[t])
                            {
                                case BatimentType.Dur:
                                    batimentDurCount++;
                                    surfaceBatieDur += area;
                                    break;
                                case BatimentType.Leger:
                                    batimentlegerCount++;
                                    surfaceBatieLeger += area;
                                    break;
                            }
                        }
                    }
                }

                if (batimentCount > 0)
                {
                    parcellesBatiesCount++;
                    surfaceBatieTotale += surfaceBatie;
                }

                string[] columns = new string[11];

                try
                {
                    columns[0] = (string)feature.Attributes["section"];
                }
                catch (Exception)
                {
                    erreurs++;
                    columns[0] = "erreur";
                }

                try
                {
                    columns[1] = (string)feature.Attributes["numero"];
                }
                catch (Exception)
                {
                    erreurs++;
                    columns[1] = "erreur";
                }

                columns[2] = $"{centroid.Y.ToString("0.000000")}";
                columns[3] = $"{centroid.X.ToString("0.000000")}";

                try
                {
                    long surface = (long)feature.Attributes["contenance"];
                    surfaceParcelles += surface;
                    columns[4] = surface.ToString();
                }
                catch (Exception)
                {
                    erreurs++;
                    columns[4] = "erreur";
                }

                columns[5] = batimentCount.ToString();
                columns[6] = $"{surfaceBatie.ToString("F1")}";
                columns[7] = batimentDurCount.ToString();
                columns[8] = $"{surfaceBatieDur.ToString("F1")}";
                columns[9] = batimentlegerCount.ToString();
                columns[10] = $"{surfaceBatieLeger.ToString("F1")}";

                parcellesSurface[i + 1] = string.Join(separateur, columns);

                Console.SetCursorPosition(0, Console.CursorTop - 1);
                Console.WriteLine($"Calcul en cours : {i + 1} / {parcelleCount} parcelles ({erreurs.ToString()} erreurs)");
            }


            Console.SetCursorPosition(0, Console.CursorTop - 3);
            Console.WriteLine("-------------------------------------------------------------------------------------------");
            Console.WriteLine($"Calcul terminé avec {erreurs} erreur(s)                           ");
            Console.WriteLine("-------------------------------------------------------------------------------------------");
            Console.WriteLine($"Batiments : {batiments.Features.Count}, parcelles : {parcelleCount}, dont baties : {parcellesBatiesCount}.");
            Console.WriteLine($"Surface cadastrée : {(surfaceParcelles / 1000000).ToString("0.000 km²")}, surface batie : {(surfaceBatieTotale / 1000000).ToString("0.000 km²")} ({(surfaceBatieTotale / surfaceParcelles).ToString("P2")})");
            string resultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), fileName);
            File.WriteAllLines(resultPath, parcellesSurface);
            Console.WriteLine("-------------------------------------------------------------------------------------------");
            Console.WriteLine($"Fichier CSV créé : {resultPath}");
            Console.WriteLine("-------------------------------------------------------------------------------------------");
            Console.WriteLine($"Appuyer sur [R] pour recomencer, ou tout autre touche pour quitter.");
            if (Console.ReadKey().KeyChar.ToString().ToLower() == "r")
            {
                goto start;
            }
        }


    }



    public struct LatLng
    {
        public double Latitude;
        public double Longitude;

        public LatLng(double latitude, double longitude)
        {
            Latitude = latitude;
            Longitude = longitude;
        }
    }

    public static class SphericalUtil
    {
        const double EARTH_RADIUS = 6371009;

        static double ToRadians(double input)
        {
            return input / 180.0 * Math.PI;
        }

        public static double ComputeSignedArea(IList<Coordinate> path)
        {
            return ComputeSignedArea(path, EARTH_RADIUS);
        }

        static double ComputeSignedArea(IList<Coordinate> path, double radius)
        {
            int size = path.Count;
            if (size < 3) { return 0; }
            double total = 0;
            var prev = path[size - 1];
            double prevTanLat = Math.Tan((Math.PI / 2 - ToRadians(prev.Y)) / 2);
            double prevLng = ToRadians(prev.X);

            foreach (var point in path)
            {
                double tanLat = Math.Tan((Math.PI / 2 - ToRadians(point.Y)) / 2);
                double lng = ToRadians(point.X);
                total += PolarTriangleArea(tanLat, lng, prevTanLat, prevLng);
                prevTanLat = tanLat;
                prevLng = lng;
            }
            return total * (radius * radius);
        }

        static double PolarTriangleArea(double tan1, double lng1, double tan2, double lng2)
        {
            double deltaLng = lng1 - lng2;
            double t = tan1 * tan2;
            return 2 * Math.Atan2(t * Math.Sin(deltaLng), 1 + t * Math.Cos(deltaLng));
        }
    }
}
