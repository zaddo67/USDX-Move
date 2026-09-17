using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using USDX_Move.Models;

namespace USDX_Move.Services
{
    public static class SongTxtParser
    {
        /// <summary>
        /// Finds the best matching .txt file in the folder, parses #TITLE, #ARTIST, #COVER, #BACKGROUND.
        /// </summary>
        public static (string? TxtPath, string Artist, string Title, string? Cover, string? Background) ParseSongFolder(string folderPath, string folderName)
        {
            if (!Directory.Exists(folderPath))
                return (null, "", "", null, null);

            var txtFiles = Directory.GetFiles(folderPath, "*.txt");
            if (txtFiles.Length == 0)
                return (null, "", "", null, null);

            string bestTxt = FindBestMatchingTxtFile(folderName, txtFiles);
            if (string.IsNullOrEmpty(bestTxt) || !File.Exists(bestTxt))
                return (null, "", "", null, null);

            var (artist, title, cover, background) = ParseTxtFile(bestTxt);
            return (bestTxt, artist, title, cover, background);
        }

        private static string FindBestMatchingTxtFile(string folderName, string[] txtFiles)
        {
            if (txtFiles.Length == 1)
                return txtFiles[0];

            string targetUnderscore = folderName.Replace(' ', '_');

            // 1. Exact match with spaces replaced by underscores (e.g., Aerosmith_-_Blind_man.txt)
            var match1 = txtFiles.FirstOrDefault(f => 
                string.Equals(Path.GetFileNameWithoutExtension(f), targetUnderscore, StringComparison.OrdinalIgnoreCase));
            if (match1 != null) return match1;

            // 2. Exact match with folder name
            var match2 = txtFiles.FirstOrDefault(f => 
                string.Equals(Path.GetFileNameWithoutExtension(f), folderName, StringComparison.OrdinalIgnoreCase));
            if (match2 != null) return match2;

            // 3. Normalized alphanumeric match
            string normTarget = Regex.Replace(folderName, @"[^a-zA-Z0-9]", "").ToLowerInvariant();
            var match3 = txtFiles.FirstOrDefault(f => 
                Regex.Replace(Path.GetFileNameWithoutExtension(f), @"[^a-zA-Z0-9]", "").ToLowerInvariant() == normTarget);
            if (match3 != null) return match3;

            // 4. Prefix match (starts with first 3-4 chars)
            string prefix = folderName.Length >= 4 ? folderName[..4] : folderName;
            var prefixMatches = txtFiles.Where(f => 
                Path.GetFileNameWithoutExtension(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            if (prefixMatches.Count == 1) return prefixMatches[0];

            // 5. Best Levenshtein distance among files containing #TITLE: or #ARTIST:
            string bestFile = txtFiles[0];
            int lowestDistance = int.MaxValue;

            foreach (var file in txtFiles)
            {
                string fn = Path.GetFileNameWithoutExtension(file);
                int dist = ComputeLevenshteinDistance(folderName.ToLowerInvariant(), fn.ToLowerInvariant());

                // Prioritize files that actually contain UltraStar headers
                try
                {
                    string content = File.ReadAllText(file);
                    if (content.Contains("#TITLE", StringComparison.OrdinalIgnoreCase) || 
                        content.Contains("#ARTIST", StringComparison.OrdinalIgnoreCase))
                    {
                        dist -= 5; // Bonus for valid song header
                    }
                }
                catch { }

                if (dist < lowestDistance)
                {
                    lowestDistance = dist;
                    bestFile = file;
                }
            }

            return bestFile;
        }

        private static (string Artist, string Title, string? Cover, string? Background) ParseTxtFile(string txtPath)
        {
            string artist = "";
            string title = "";
            string? cover = null;
            string? background = null;

            try
            {
                var lines = File.ReadAllLines(txtPath, DetectEncoding(txtPath));
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("#ARTIST:", StringComparison.OrdinalIgnoreCase))
                    {
                        artist = trimmed[8..].Trim();
                    }
                    else if (trimmed.StartsWith("#TITLE:", StringComparison.OrdinalIgnoreCase))
                    {
                        title = trimmed[7..].Trim();
                    }
                    else if (trimmed.StartsWith("#COVER:", StringComparison.OrdinalIgnoreCase))
                    {
                        cover = trimmed[7..].Trim();
                    }
                    else if (trimmed.StartsWith("#BACKGROUND:", StringComparison.OrdinalIgnoreCase))
                    {
                        background = trimmed[12..].Trim();
                    }
                }
            }
            catch { }

            return (artist, title, cover, background);
        }

        /// <summary>
        /// Updates #COVER and #BACKGROUND tags in the song .txt file, inserting them if they don't exist.
        /// </summary>
        public static void UpdateCoverAndBackgroundTags(string txtPath, string imageFileName)
        {
            if (!File.Exists(txtPath)) return;

            var encoding = DetectEncoding(txtPath);
            var lines = File.ReadAllLines(txtPath, encoding).ToList();

            bool coverUpdated = false;
            bool backgroundUpdated = false;
            int insertIndex = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith("#COVER:", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"#COVER:{imageFileName}";
                    coverUpdated = true;
                }
                else if (trimmed.StartsWith("#BACKGROUND:", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"#BACKGROUND:{imageFileName}";
                    backgroundUpdated = true;
                }
                else if (trimmed.StartsWith("#TITLE:", StringComparison.OrdinalIgnoreCase) || 
                         trimmed.StartsWith("#ARTIST:", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.StartsWith("#MP3:", StringComparison.OrdinalIgnoreCase))
                {
                    insertIndex = i + 1;
                }
            }

            if (insertIndex < 0) insertIndex = 0;

            if (!coverUpdated)
            {
                lines.Insert(insertIndex, $"#COVER:{imageFileName}");
                insertIndex++;
            }

            if (!backgroundUpdated)
            {
                lines.Insert(insertIndex, $"#BACKGROUND:{imageFileName}");
            }

            File.WriteAllLines(txtPath, lines, encoding);
        }

        /// <summary>
        /// Updates or inserts only the #BACKGROUND tag in the song .txt file.
        /// </summary>
        public static void UpdateBackgroundTag(string txtPath, string backgroundFileName)
        {
            if (!File.Exists(txtPath)) return;

            var encoding = DetectEncoding(txtPath);
            var lines = File.ReadAllLines(txtPath, encoding).ToList();

            bool backgroundUpdated = false;
            int insertIndex = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith("#BACKGROUND:", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"#BACKGROUND:{backgroundFileName}";
                    backgroundUpdated = true;
                    break;
                }
                if (trimmed.StartsWith("#COVER:", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("#TITLE:", StringComparison.OrdinalIgnoreCase) || 
                    trimmed.StartsWith("#ARTIST:", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("#MP3:", StringComparison.OrdinalIgnoreCase))
                {
                    insertIndex = i + 1;
                }
            }

            if (!backgroundUpdated)
            {
                if (insertIndex < 0) insertIndex = 0;
                lines.Insert(insertIndex, $"#BACKGROUND:{backgroundFileName}");
            }

            File.WriteAllLines(txtPath, lines, encoding);
        }

        private static Encoding DetectEncoding(string filename)
        {
            // Read preamble to check UTF-8 BOM
            var bom = new byte[4];
            using (var file = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                file.ReadAtLeast(bom, Math.Min(4, (int)file.Length), false);
            }

            if (bom[0] == 0xef && bom[1] == 0xbb && bom[2] == 0xbf) return Encoding.UTF8;
            if (bom[0] == 0xff && bom[1] == 0xfe) return Encoding.Unicode;
            if (bom[0] == 0xfe && bom[1] == 0xff) return Encoding.BigEndianUnicode;

            // Default UTF-8 without BOM or system ANSI
            return new UTF8Encoding(false);
        }

        private static int ComputeLevenshteinDistance(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            if (n == 0) return m;
            if (m == 0) return n;

            for (int i = 0; i <= n; d[i, 0] = i++) { }
            for (int j = 0; j <= m; d[0, j] = j++) { }

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[n, m];
        }
    }
}
