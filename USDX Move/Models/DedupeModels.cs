using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace USDX_Move.Models
{
    public class DuplicateSongVersion : INotifyPropertyChanged
    {
        public string FolderName { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string TxtFilePath { get; set; } = string.Empty;

        public string Artist { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string VideoTag { get; set; } = string.Empty;
        public string CoverTag { get; set; } = string.Empty;
        public string BackgroundTag { get; set; } = string.Empty;

        public bool HasVideo => !string.IsNullOrWhiteSpace(VideoTag);
        public bool HasCover => !string.IsNullOrWhiteSpace(CoverTag);
        public bool HasBackground => !string.IsNullOrWhiteSpace(BackgroundTag);

        public int QualityScore { get; set; }

        private bool _isKeep;
        public bool IsKeep
        {
            get => _isKeep;
            set { _isKeep = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsTrash)); OnPropertyChanged(nameof(DecisionText)); }
        }

        public bool IsTrash
        {
            get => !IsKeep;
            set { IsKeep = !value; }
        }

        public string DecisionText => IsKeep ? "Keep Version" : "Move to Trash";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class DuplicateSongGroup : INotifyPropertyChanged
    {
        public string NormalizedKey { get; set; } = string.Empty;
        public string DisplayArtist { get; set; } = string.Empty;
        public string DisplayTitle { get; set; } = string.Empty;

        public ObservableCollection<DuplicateSongVersion> Versions { get; } = new();

        public int TotalCopies => Versions.Count;
        public int TrashCount => Versions.Count(v => v.IsTrash);

        public string SummaryText
        {
            get
            {
                int withVideo = Versions.Count(v => v.HasVideo);
                if (withVideo > 0)
                {
                    return $"{TotalCopies} copies ({withVideo} with Video)";
                }
                return $"{TotalCopies} copies (Cover/Audio only)";
            }
        }

        public void SetKeep(DuplicateSongVersion selectedVersion)
        {
            foreach (var v in Versions)
            {
                v.IsKeep = (v == selectedVersion);
            }
            OnPropertyChanged(nameof(TrashCount));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
