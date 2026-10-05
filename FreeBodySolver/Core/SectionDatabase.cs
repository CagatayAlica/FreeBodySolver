using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FreeBodySolver.Models;

namespace FreeBodySolver.Core
{
    public static class SectionDatabase
    {
        private static string filePath = "section_library.dat";

        // Kayıtlı profilleri dosyadan okur
        public static List<SectionProfile> LoadProfiles()
        {
            var list = new List<SectionProfile>();
            if (!File.Exists(filePath)) return list;

            var lines = File.ReadAllLines(filePath);
            foreach (var line in lines)
            {
                var p = line.Split(';');
                if (p.Length >= 7)
                {
                    list.Add(new SectionProfile
                    {
                        Name = p[0],
                        ShapeType = p[1],
                        A = float.Parse(p[2], CultureInfo.InvariantCulture),
                        B = float.Parse(p[3], CultureInfo.InvariantCulture),
                        C = float.Parse(p[4], CultureInfo.InvariantCulture),
                        R = float.Parse(p[5], CultureInfo.InvariantCulture),
                        t = float.Parse(p[6], CultureInfo.InvariantCulture)
                    });
                }
            }
            // İsim sırasına göre alfabetik dizip döndürür
            return list.OrderBy(x => x.Name).ToList();
        }

        // Yeni profili kaydeder (Aynı isim varsa üzerine yazar)
        public static void SaveProfile(SectionProfile profile)
        {
            var list = LoadProfiles();
            list.RemoveAll(x => x.Name.ToLower() == profile.Name.ToLower());
            list.Add(profile);

            WriteToFile(list);
        }

        // Seçilen profili siler
        public static void DeleteProfile(string name)
        {
            var list = LoadProfiles();
            list.RemoveAll(x => x.Name == name);
            WriteToFile(list);
        }

        private static void WriteToFile(List<SectionProfile> list)
        {
            using (StreamWriter sw = new StreamWriter(filePath))
            {
                foreach (var prof in list)
                {
                    sw.WriteLine($"{prof.Name};{prof.ShapeType};" +
                                 $"{prof.A.ToString(CultureInfo.InvariantCulture)};" +
                                 $"{prof.B.ToString(CultureInfo.InvariantCulture)};" +
                                 $"{prof.C.ToString(CultureInfo.InvariantCulture)};" +
                                 $"{prof.R.ToString(CultureInfo.InvariantCulture)};" +
                                 $"{prof.t.ToString(CultureInfo.InvariantCulture)}");
                }
            }
        }
    }
}
