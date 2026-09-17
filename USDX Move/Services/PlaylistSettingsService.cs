using System;
using System.IO;
using System.Text.Json;
using USDX_Move.Models;

namespace USDX_Move.Services
{
    public sealed class PlaylistSettingsService
    {
        private readonly string _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "USDX Move", "playlist-settings.json");

        public PlaylistSettings Load()
        {
            try
            {
                return JsonSerializer.Deserialize<PlaylistSettings>(File.ReadAllText(_settingsPath)) ?? new PlaylistSettings();
            }
            catch
            {
                return new PlaylistSettings();
            }
        }

        public void Save(PlaylistSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings));
        }
    }
}
