using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace USDX_Move.Models
{
    public class FixBackgroundItem : INotifyPropertyChanged
    {
        public string FolderName { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string TxtFilePath { get; set; } = string.Empty;

        public string Artist { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;

        public string CoverFileName { get; set; } = string.Empty;
        public string? CoverImageFullPath { get; set; }
        public string? CurrentBackgroundTag { get; set; }

        private bool _isFixed;
        public bool IsFixed
        {
            get => _isFixed;
            set { _isFixed = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public override string ToString() => $"{Artist} - {Title}";
    }
}
