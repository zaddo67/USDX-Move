using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using USDX_Move.Models;

namespace USDX_Move.Services
{
    public sealed class PlaylistFileService
    {
        public IReadOnlyList<PlaylistDefinition> GetPlaylists(string folderPath) =>
            !Directory.Exists(folderPath) ? [] : Directory.GetFiles(folderPath, "*.upl")
                .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.CurrentCultureIgnoreCase)
                .Select(Load)
                .ToList();

        public PlaylistDefinition Load(string filePath)
        {
            var playlist = new PlaylistDefinition
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath
            };

            bool inSongs = false;
            foreach (string rawLine in File.ReadLines(filePath))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("#Name:", StringComparison.OrdinalIgnoreCase))
                {
                    playlist = new PlaylistDefinition { Name = line[6..].Trim(), FilePath = filePath };
                }
                else if (line.Equals("#Songs:", StringComparison.OrdinalIgnoreCase))
                {
                    inSongs = true;
                }
                else if (inSongs && !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
                {
                    playlist.SongEntries.Add(line);
                }
            }
            return playlist;
        }

        public string Save(string folderPath, string playlistName, IEnumerable<PlaylistSongItem> songs)
        {
            string name = playlistName.Trim();
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a playlist name.");
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("The playlist name contains an invalid file name character.");

            Directory.CreateDirectory(folderPath);
            var entries = songs.Select(s => s.PlaylistEntry).ToList();
            var lines = new List<string>
            {
                "######################################",
                "#Ultrastar Deluxe Playlist Format v1.0",
                $"#Playlist {name} with {entries.Count} Songs.",
                "######################################",
                $"#Name: {name}",
                "#FixedOrder: Off",
                "#Songs:"
            };
            lines.AddRange(entries);
            string filePath = Path.Combine(folderPath, $"{name}.upl");
            File.WriteAllLines(filePath, lines);
            return filePath;
        }

        public void Delete(string filePath)
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }
}
