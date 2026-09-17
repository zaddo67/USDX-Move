using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using USDX_Move.Models;

namespace USDX_Move.Services
{
    public class DedupeService
    {
        public List<DuplicateSongGroup> ScanAndFindDuplicates(string rootPath, CancellationToken cancellationToken = default)
        {
            var allSongs = new List<DuplicateSongVersion>();

            var enumOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System
            };

            var allDirs = Directory.GetDirectories(rootPath, "*", enumOptions);

            foreach (var dir in allDirs)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    string folderName = Path.GetFileName(dir);
                    var txtFiles = Directory.GetFiles(dir, "*.txt");
                    if (txtFiles.Length == 0) continue;

                    string? bestTxt = null;
                    string artist = "";
                    string title = "";
                    string video = "";
                    string? cover = "";
                    string? background = "";

                    // Find best matching txt file
                    (bestTxt, artist, title, cover, background) = SongTxtParser.ParseSongFolder(dir, folderName);

                    if (string.IsNullOrEmpty(bestTxt) || !File.Exists(bestTxt)) continue;

                    // Read #VIDEO tag from txt file
                    try
                    {
                        var lines = File.ReadAllLines(bestTxt);
                        foreach (var rawLine in lines)
                        {
                            var line = rawLine.Trim();
                            if (line.StartsWith("#VIDEO:", StringComparison.OrdinalIgnoreCase))
                            {
                                video = line[7..].Trim();
                                break;
                            }
                        }
                    }
                    catch { }

                    // Fallback to folder name parsing if metadata tags are blank
                    if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title))
                    {
                        var parts = folderName.Split(new[] { " - ", " _ " }, 2, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2)
                        {
                            artist = parts[0].Trim();
                            title = parts[1].Trim();
                        }
                        else
                        {
                            title = folderName;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title))
                        continue;

                    // Quality scoring:
                    // Priority 1: Has Video (+100, AVI bonus +15)
                    // Priority 2: Has Cover (+20) / Background (+10)
                    int score = 10;
                    if (!string.IsNullOrWhiteSpace(video))
                    {
                        score += 100;
                        if (video.EndsWith(".avi", StringComparison.OrdinalIgnoreCase))
                        {
                            score += 15; // Extra preference for AVI
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(cover)) score += 20;
                    if (!string.IsNullOrWhiteSpace(background)) score += 10;

                    allSongs.Add(new DuplicateSongVersion
                    {
                        FolderName = folderName,
                        FolderPath = dir,
                        RelativePath = Path.GetRelativePath(rootPath, dir),
                        TxtFilePath = bestTxt,
                        Artist = artist,
                        Title = title,
                        VideoTag = video,
                        CoverTag = cover ?? "",
                        BackgroundTag = background ?? "",
                        QualityScore = score
                    });
                }
                catch { }
            }

            if (cancellationToken.IsCancellationRequested)
                return new List<DuplicateSongGroup>();

            // Group by normalized Artist + Title
            var groups = allSongs
                .GroupBy(s => NormalizeKey(s.Artist, s.Title))
                .Where(g => g.Count() >= 2)
                .OrderBy(g => g.First().Artist, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(g => g.First().Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var result = new List<DuplicateSongGroup>();

            foreach (var g in groups)
            {
                var sample = g.First();
                var dupGroup = new DuplicateSongGroup
                {
                    NormalizedKey = g.Key,
                    DisplayArtist = sample.Artist,
                    DisplayTitle = sample.Title
                };

                // Sort versions by quality score descending (best version first)
                var sortedVersions = g.OrderByDescending(v => v.QualityScore)
                                      .ThenBy(v => v.FolderName, StringComparer.CurrentCultureIgnoreCase)
                                      .ToList();

                // Top version is KEEP, others are TRASH
                for (int i = 0; i < sortedVersions.Count; i++)
                {
                    sortedVersions[i].IsKeep = (i == 0);
                    dupGroup.Versions.Add(sortedVersions[i]);
                }

                result.Add(dupGroup);
            }

            return result;
        }

        private static string NormalizeKey(string artist, string title)
        {
            string cleanArtist = Regex.Replace(artist.Trim().ToLowerInvariant(), @"\s+", " ");
            string cleanTitle = Regex.Replace(title.Trim().ToLowerInvariant(), @"\s+", " ");
            return $"{cleanArtist}____{cleanTitle}";
        }
    }
}
