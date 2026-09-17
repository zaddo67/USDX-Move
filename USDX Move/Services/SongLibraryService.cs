using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using USDX_Move.Models;

namespace USDX_Move.Services
{
    public sealed class SongLibraryService
    {
        public List<PlaylistSongItem> Scan(string songsRoot, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(songsRoot)) return [];
            var results = new List<PlaylistSongItem>();
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };

            foreach (string folder in Directory.GetDirectories(songsRoot, "*", options))
            {
                if (cancellationToken.IsCancellationRequested) break;
                try
                {
                    string folderName = Path.GetFileName(folder);
                    var (txtPath, artist, title, cover, _) = SongTxtParser.ParseSongFolder(folder, folderName);
                    if (string.IsNullOrWhiteSpace(txtPath) || (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title))) continue;

                    string audioFile = ReadTag(txtPath, "#MP3:");
                    results.Add(new PlaylistSongItem
                    {
                        Artist = artist,
                        Title = title,
                        FolderPath = folder,
                        AudioPath = Path.Combine(folder, audioFile),
                        CoverPath = string.IsNullOrWhiteSpace(cover) ? string.Empty : Path.Combine(folder, cover)
                    });
                }
                catch { }
            }
            return results.OrderBy(s => s.Artist, StringComparer.CurrentCultureIgnoreCase)
                          .ThenBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static string ReadTag(string txtPath, string tag) => File.ReadLines(txtPath)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(tag, StringComparison.OrdinalIgnoreCase))
            .Select(line => line[tag.Length..].Trim())
            .FirstOrDefault() ?? string.Empty;
    }
}
