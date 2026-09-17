using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace USDX_Move.Models
{
    public class SongFolderItem : INotifyPropertyChanged
    {
        public string FolderName { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string? TxtFilePath { get; set; }
        public string Artist { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? CoverTag { get; set; }
        public string? BackgroundTag { get; set; }

        private bool _isResolved;
        public bool IsResolved
        {
            get => _isResolved;
            set { _isResolved = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public override string ToString() => FolderName;
    }

    public class CoverArtCandidate : INotifyPropertyChanged
    {
        public string ImageUrl { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Resolution { get; set; } = string.Empty;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
