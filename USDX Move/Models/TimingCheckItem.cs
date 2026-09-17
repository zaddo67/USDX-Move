using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace USDX_Move.Models
{
    public enum TimingStatus
    {
        Pending,
        Ok,
        TimingIssue,
        NoReferenceFound,
        Error
    }

    public class TimingCheckItem : INotifyPropertyChanged
    {
        public string FolderName { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string? TxtFilePath { get; set; }

        public string Artist { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;

        public double GapMs { get; set; }
        public double Bpm { get; set; }
        public int FirstBeat { get; set; }
        public string FirstLyricWord { get; set; } = string.Empty;
        public double CalculatedFirstVocalSec { get; set; }

        public double? ReferenceFirstVocalSec { get; set; }
        public string ReferenceFirstLine { get; set; } = string.Empty;
        public double? DeltaSec { get; set; }

        private bool _isMarkedForTrash;
        public bool IsMarkedForTrash
        {
            get => _isMarkedForTrash;
            set { _isMarkedForTrash = value; OnPropertyChanged(); }
        }

        private TimingStatus _status = TimingStatus.Pending;
        public TimingStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(DeltaText)); }
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public string StatusText => Status switch
        {
            TimingStatus.Ok => "Timing OK",
            TimingStatus.TimingIssue => "Timing Issue",
            TimingStatus.NoReferenceFound => "No Online Reference",
            TimingStatus.Pending => "Checking...",
            TimingStatus.Error => "Error",
            _ => ""
        };

        public string DeltaText
        {
            get
            {
                if (!DeltaSec.HasValue) return "";
                string sign = DeltaSec.Value >= 0 ? "+" : "";
                return $"Δ {sign}{DeltaSec.Value:0.00}s";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public override string ToString() => $"{Artist} - {Title}";
    }
}
