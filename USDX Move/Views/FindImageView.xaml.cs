using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using USDX_Move.Models;
using USDX_Move.Services;

namespace USDX_Move.Views
{
    /// <summary>
    /// Interaction logic for FindImageView.xaml
    /// </summary>
    public partial class FindImageView : UserControl
    {
        public event EventHandler? BackRequested;

        private readonly ObservableCollection<SongFolderItem> _missingFolders = new();
        private readonly ObservableCollection<CoverArtCandidate> _candidateImages = new();
        private readonly ICollectionView _missingView;
        private readonly CoverArtService _coverArtService = new();

        private SongFolderItem? _selectedSong;
        private CancellationTokenSource? _searchCts;

        public FindImageView()
        {
            InitializeComponent();

            _missingView = CollectionViewSource.GetDefaultView(_missingFolders);
            _missingView.Filter = FilterMissingFolder;

            LstMissingFolders.ItemsSource = _missingView;
            LstCandidateImages.ItemsSource = _candidateImages;

            _missingFolders.CollectionChanged += (s, e) => UpdateCounts();
            UpdateCounts();
        }

        private void BtnBackToMenu_Click(object sender, RoutedEventArgs e)
        {
            BackRequested?.Invoke(this, EventArgs.Empty);
        }

        private bool FilterMissingFolder(object item)
        {
            if (string.IsNullOrWhiteSpace(TxtFilter?.Text))
                return true;

            string query = TxtFilter.Text.Trim();
            if (item is SongFolderItem song)
            {
                return song.FolderName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       song.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       song.Title.Contains(query, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            _missingView.Refresh();
            UpdateCounts();
        }

        private void BtnClearFilter_Click(object sender, RoutedEventArgs e)
        {
            TxtFilter.Text = string.Empty;
        }

        private void UpdateCounts()
        {
            int total = _missingFolders.Count;
            bool isFiltering = !string.IsNullOrWhiteSpace(TxtFilter?.Text);

            if (isFiltering)
            {
                int visible = _missingFolders.Count(FilterMissingFolder);
                TxtMissingCount.Text = $"{visible} of {total} folder{(total == 1 ? "" : "s")}";
            }
            else
            {
                TxtMissingCount.Text = $"{total} folder{(total == 1 ? "" : "s")}";
            }
        }

        #region Scanning

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Songs Root Folder",
                Multiselect = false
            };

            if (!string.IsNullOrWhiteSpace(TxtScanFolder.Text) && Directory.Exists(TxtScanFolder.Text))
            {
                dialog.InitialDirectory = TxtScanFolder.Text;
            }

            if (dialog.ShowDialog() == true)
            {
                TxtScanFolder.Text = dialog.FolderName;
            }
        }

        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            string rootPath = TxtScanFolder.Text.Trim();
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
            {
                MessageBox.Show("Please select a valid songs root folder.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetBusyState(true);
            ProgScan.Visibility = Visibility.Visible;
            _missingFolders.Clear();
            _candidateImages.Clear();
            _selectedSong = null;
            TxtArtist.Text = "";
            TxtTitle.Text = "";
            TxtMatchedFile.Text = "None selected";
            TxtStatus.Text = "Scanning folders recursively for missing images...";

            var discoveredItems = new List<SongFolderItem>();

            await Task.Run(() =>
            {
                var enumOptions = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.System
                };

                string[] allDirs;
                try
                {
                    allDirs = Directory.GetDirectories(rootPath, "*", enumOptions);
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => TxtStatus.Text = $"Error scanning folders: {ex.Message}");
                    return;
                }

                foreach (var dir in allDirs)
                {
                    try
                    {
                        // 1. Must contain an audio file (.mp3, .ogg, .m4a)
                        var audioFiles = Directory.GetFiles(dir, "*.*")
                            .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (audioFiles.Count == 0)
                            continue; // Not a song folder

                        // 2. Check if it contains an image (.jpg, .jpeg, .png) or video (.avi, .mp4, .mkv)
                        bool hasImageOrVideo = Directory.GetFiles(dir, "*.*")
                            .Any(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                      f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                      f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                      f.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
                                      f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                      f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase));

                        if (hasImageOrVideo)
                            continue; // Already has image or video

                        // 3. Missing image/video: Parse metadata from .txt file
                        string folderName = Path.GetFileName(dir);
                        var (txtPath, artist, title, cover, background) = SongTxtParser.ParseSongFolder(dir, folderName);

                        // If artist/title not found in .txt, attempt to extract from folder name "Artist - Title"
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

                        discoveredItems.Add(new SongFolderItem
                        {
                            FolderName = folderName,
                            FolderPath = dir,
                            RelativePath = Path.GetRelativePath(rootPath, dir),
                            TxtFilePath = txtPath,
                            Artist = artist,
                            Title = title,
                            CoverTag = cover,
                            BackgroundTag = background
                        });
                    }
                    catch { }
                }
            });

            foreach (var item in discoveredItems.OrderBy(x => x.FolderName, StringComparer.CurrentCultureIgnoreCase))
            {
                _missingFolders.Add(item);
            }

            SetBusyState(false);
            ProgScan.Visibility = Visibility.Collapsed;
            _missingView.Refresh();

            TxtStatus.Text = $"Scan complete. Found {_missingFolders.Count} folder(s) missing cover image/video.";
        }

        #endregion

        #region Selection & Online Search

        private async void LstMissingFolders_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstMissingFolders.SelectedItem is SongFolderItem song)
            {
                _selectedSong = song;
                TxtArtist.Text = song.Artist;
                TxtTitle.Text = song.Title;
                TxtMatchedFile.Text = string.IsNullOrEmpty(song.TxtFilePath) ? "No .txt file found" : Path.GetFileName(song.TxtFilePath);
                TxtSaveFeedback.Text = "";

                await ExecuteImageSearchAsync(song.Artist, song.Title);
            }
        }

        private async void BtnSearchAgain_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSong != null)
            {
                await ExecuteImageSearchAsync(TxtArtist.Text.Trim(), TxtTitle.Text.Trim());
            }
        }

        private async Task ExecuteImageSearchAsync(string artist, string title)
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            _candidateImages.Clear();
            PnlLoading.Visibility = Visibility.Visible;
            PnlNoImages.Visibility = Visibility.Collapsed;
            BtnSaveImage.IsEnabled = false;

            try
            {
                var results = await _coverArtService.SearchCoverArtAsync(artist, title, ct);

                if (ct.IsCancellationRequested) return;

                PnlLoading.Visibility = Visibility.Collapsed;

                if (results.Count == 0)
                {
                    PnlNoImages.Visibility = Visibility.Visible;
                }
                else
                {
                    PnlNoImages.Visibility = Visibility.Collapsed;
                    // Auto-select first candidate
                    results[0].IsSelected = true;

                    foreach (var item in results)
                    {
                        _candidateImages.Add(item);
                    }

                    BtnSaveImage.IsEnabled = true;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                PnlLoading.Visibility = Visibility.Collapsed;
                PnlNoImages.Visibility = Visibility.Visible;
                TxtStatus.Text = $"Image search error: {ex.Message}";
            }
        }

        private void CandidateImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is CoverArtCandidate clicked)
            {
                foreach (var candidate in _candidateImages)
                {
                    candidate.IsSelected = (candidate == clicked);
                }
                BtnSaveImage.IsEnabled = true;
            }
        }

        #endregion

        #region Save & Apply

        private async void BtnSaveImage_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSong == null)
            {
                MessageBox.Show("Please select a song folder first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var chosen = _candidateImages.FirstOrDefault(c => c.IsSelected);
            if (chosen == null)
            {
                MessageBox.Show("Please click on one of the candidate images to select it.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SetBusyState(true);
            TxtSaveFeedback.Text = "Downloading image...";
            TxtSaveFeedback.Foreground = System.Windows.Media.Brushes.DarkOrange;

            try
            {
                string targetFileName = "cover.jpg";
                string destImagePath = Path.Combine(_selectedSong.FolderPath, targetFileName);

                // 1. Download image to song folder
                await _coverArtService.DownloadImageAsync(chosen.ImageUrl, destImagePath);

                // 2. Update .txt file if exists
                if (!string.IsNullOrEmpty(_selectedSong.TxtFilePath) && File.Exists(_selectedSong.TxtFilePath))
                {
                    SongTxtParser.UpdateCoverAndBackgroundTags(_selectedSong.TxtFilePath, targetFileName);
                }

                // 3. Mark as resolved
                _selectedSong.IsResolved = true;
                _selectedSong.CoverTag = targetFileName;
                _selectedSong.BackgroundTag = targetFileName;

                TxtSaveFeedback.Foreground = System.Windows.Media.Brushes.Green;
                TxtSaveFeedback.Text = $"✓ Saved cover.jpg & updated .txt file!";
                TxtStatus.Text = $"Saved image for '{_selectedSong.FolderName}'";
            }
            catch (Exception ex)
            {
                TxtSaveFeedback.Foreground = System.Windows.Media.Brushes.Red;
                TxtSaveFeedback.Text = $"Error: {ex.Message}";
                MessageBox.Show($"Failed to save image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusyState(false);
            }
        }

        #endregion

        private void SetBusyState(bool isBusy)
        {
            BtnBackToMenu.IsEnabled = !isBusy;
            BtnBrowse.IsEnabled = !isBusy;
            BtnScan.IsEnabled = !isBusy;
            TxtScanFolder.IsEnabled = !isBusy;
            Cursor = isBusy ? Cursors.Wait : Cursors.Arrow;
        }
    }
}
