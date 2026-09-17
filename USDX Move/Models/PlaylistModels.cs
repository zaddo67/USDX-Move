using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace USDX_Move.Models
{
    public sealed class PlaylistSongItem : INotifyPropertyChanged
    {
        public string Artist { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string FolderPath { get; init; } = string.Empty;
        public string AudioPath { get; init; } = string.Empty;
        public string CoverPath { get; init; } = string.Empty;
        public string PlaylistEntry => $"{Artist} : {Title}";
        public string DisplayName => $"{Artist} - {Title}";

        private bool _isInPlaylist;
        public bool IsInPlaylist
        {
            get => _isInPlaylist;
            set { _isInPlaylist = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlaylistStatus)); }
        }

        public string PlaylistStatus => IsInPlaylist ? "Added" : string.Empty;
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class PlaylistDefinition
    {
        public string Name { get; init; } = string.Empty;
        public string FilePath { get; init; } = string.Empty;
        public List<string> SongEntries { get; } = [];
    }

    public sealed class PlaylistSettings
    {
        public string SongsFolder { get; set; } = string.Empty;
        public string PlaylistsFolder { get; set; } = string.Empty;
    }
}
