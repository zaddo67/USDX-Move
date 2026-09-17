using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using USDX_Move.Models;
using USDX_Move.Services;

namespace USDX_Move.Views
{
    /// <summary>
    /// Interaction logic for CheckTimingView.xaml
    /// </summary>
    public partial class CheckTimingView : UserControl
    {
        public event EventHandler? BackRequested;

        private readonly ObservableCollection<TimingCheckItem> _allSongs = new();
        private readonly ICollectionView _songsView;
        private readonly TimingCheckService _timingService = new();

        private CancellationTokenSource? _scanCts;
        private TimingCheckItem? _selectedItem;

        public CheckTimingView()
        {
            _songsView = CollectionViewSource.GetDefaultView(_allSongs);
            _songsView.Filter = FilterSongItem;

            InitializeComponent();

            LstSongs.ItemsSource = _songsView;

            _allSongs.CollectionChanged += (s, e) => UpdateFilterCounts();
            UpdateFilterCounts();
        }

        private void BtnBackToMenu_Click(object sender, RoutedEventArgs e)
        {
            _scanCts?.Cancel();
            BackRequested?.Invoke(this, EventArgs.Empty);
        }

        private double GetTolerance()
        {
            if (CmbTolerance?.SelectedItem is ComboBoxItem item && item.Content is string s)
            {
                string num = s.Replace("s", "").Trim();
                if (double.TryParse(num, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                {
                    return val;
                }
            }
            return 2.0;
        }

        private void CmbTolerance_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_allSongs.Count > 0)
            {
                double tolerance = GetTolerance();
                foreach (var song in _allSongs)
                {
                    if (song.DeltaSec.HasValue)
                    {
                        if (Math.Abs(song.DeltaSec.Value) > tolerance)
                        {
                            song.Status = TimingStatus.TimingIssue;
                            song.StatusMessage = song.DeltaSec.Value > 0
                                ? $"Starts ~{song.DeltaSec.Value:0.1}s too late compared to reference"
                                : $"Starts ~{Math.Abs(song.DeltaSec.Value):0.1}s too early compared to reference";
                        }
                        else
                        {
                            song.Status = TimingStatus.Ok;
                            song.StatusMessage = $"Timing matches online reference (within {Math.Abs(song.DeltaSec.Value):0.2}s).";
                        }
                    }
                }
                _songsView?.Refresh();
                UpdateFilterCounts();
                if (_selectedItem != null) DisplaySongDetails(_selectedItem);
            }
        }

        #region Filtering

        private bool FilterSongItem(object obj)
        {
            if (obj is not TimingCheckItem song) return false;

            // 1. Radio status filter
            if (RadFilterTrash?.IsChecked == true && !song.IsMarkedForTrash) return false;
            if (RadFilterIssues?.IsChecked == true && song.Status != TimingStatus.TimingIssue) return false;
            if (RadFilterOk?.IsChecked == true && song.Status != TimingStatus.Ok) return false;
            if (RadFilterUnmatched?.IsChecked == true && song.Status != TimingStatus.NoReferenceFound && song.Status != TimingStatus.Error) return false;

            // 2. Text query filter
            if (!string.IsNullOrWhiteSpace(TxtFilter?.Text))
            {
                string query = TxtFilter.Text.Trim();
                return song.FolderName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       song.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       song.Title.Contains(query, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private void FilterRadio_Checked(object sender, RoutedEventArgs e)
        {
            _songsView?.Refresh();
        }

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            _songsView?.Refresh();
        }

        private void BtnClearFilter_Click(object sender, RoutedEventArgs e)
        {
            TxtFilter.Text = string.Empty;
        }

        private void UpdateFilterCounts()
        {
            int total = _allSongs.Count;
            int issues = _allSongs.Count(s => s.Status == TimingStatus.TimingIssue);
            int ok = _allSongs.Count(s => s.Status == TimingStatus.Ok);
            int unmatched = _allSongs.Count(s => s.Status == TimingStatus.NoReferenceFound || s.Status == TimingStatus.Error);
            int trash = _allSongs.Count(s => s.IsMarkedForTrash);

            if (RadFilterAll != null) RadFilterAll.Content = $"All ({total})";
            if (RadFilterIssues != null) RadFilterIssues.Content = $"⚠️ Issues ({issues})";
            if (RadFilterOk != null) RadFilterOk.Content = $"✓ OK ({ok})";
            if (RadFilterUnmatched != null) RadFilterUnmatched.Content = $"❓ Unmatched ({unmatched})";
            if (RadFilterTrash != null) RadFilterTrash.Content = $"🗑️ Marked for Move ({trash})";

            if (TxtMoveTrashBtn != null) TxtMoveTrashBtn.Text = $"Move Marked Songs ({trash})";
            if (BtnMoveTrash != null)
            {
                BtnMoveTrash.IsEnabled = trash > 0 && !string.IsNullOrWhiteSpace(TxtTrashFolder?.Text);
            }
        }

        #endregion

        #region Scanning & Checking

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

        private void BtnBrowseTrash_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Destination Trash Folder",
                Multiselect = false
            };

            if (!string.IsNullOrWhiteSpace(TxtTrashFolder.Text) && Directory.Exists(TxtTrashFolder.Text))
            {
                dialog.InitialDirectory = TxtTrashFolder.Text;
            }

            if (dialog.ShowDialog() == true)
            {
                TxtTrashFolder.Text = dialog.FolderName;
                UpdateFilterCounts();
            }
        }

        private void TxtTrashFolder_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateFilterCounts();
        }

        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            string rootPath = TxtScanFolder.Text.Trim();
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
            {
                MessageBox.Show("Please select a valid songs root folder.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _scanCts?.Cancel();
            _scanCts = new CancellationTokenSource();
            var ct = _scanCts.Token;

            SetBusyState(true);
            ProgScan.Visibility = Visibility.Visible;
            _allSongs.Clear();
            _selectedItem = null;
            ClearDetails();
            TxtStatus.Text = "Discovering song folders and parsing timing parameters...";

            double tolerance = GetTolerance();
            var parsedSongs = new List<TimingCheckItem>();

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
                    Dispatcher.Invoke(() => TxtStatus.Text = $"Error discovering folders: {ex.Message}");
                    return;
                }

                foreach (var dir in allDirs)
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        var hasAudio = Directory.GetFiles(dir, "*.*")
                            .Any(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                                      f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase));

                        var hasTxt = Directory.GetFiles(dir, "*.txt").Length > 0;

                        if (hasAudio && hasTxt)
                        {
                            var item = _timingService.ParseSongTiming(dir, Path.GetRelativePath(rootPath, dir));
                            parsedSongs.Add(item);
                        }
                    }
                    catch { }
                }
            }, ct);

            if (ct.IsCancellationRequested)
            {
                SetBusyState(false);
                return;
            }

            foreach (var item in parsedSongs.OrderBy(s => s.FolderName, StringComparer.CurrentCultureIgnoreCase))
            {
                _allSongs.Add(item);
            }

            TxtStatus.Text = $"Parsed {parsedSongs.Count} song(s). Checking online synced lyrics references...";
            ProgScan.Maximum = parsedSongs.Count;
            ProgScan.Value = 0;

            // Query online references with concurrency limiter
            using var semaphore = new SemaphoreSlim(4);
            int processed = 0;

            var tasks = parsedSongs.Select(async song =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    if (!ct.IsCancellationRequested && song.Status != TimingStatus.Error)
                    {
                        await _timingService.EvaluateTimingOnlineAsync(song, tolerance, ct);
                    }
                }
                finally
                {
                    semaphore.Release();
                    Interlocked.Increment(ref processed);
                    Dispatcher.Invoke(() =>
                    {
                        ProgScan.Value = processed;
                        TxtStatus.Text = $"Checking timing online ({processed}/{parsedSongs.Count})...";
                        UpdateFilterCounts();
                    });
                }
            });

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) { }

            SetBusyState(false);
            ProgScan.Visibility = Visibility.Collapsed;
            _songsView?.Refresh();
            UpdateFilterCounts();

            int issueCount = _allSongs.Count(s => s.Status == TimingStatus.TimingIssue);
            TxtStatus.Text = $"Completed! Checked {_allSongs.Count} songs. Found {issueCount} with potential timing discrepancies (tolerance > {tolerance:0.0}s).";
        }

        #endregion

        #region Details & Trash Toggle

        private void LstSongs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstSongs.SelectedItem is TimingCheckItem item)
            {
                _selectedItem = item;
                DisplaySongDetails(item);
            }
        }

        private void DisplaySongDetails(TimingCheckItem item)
        {
            TxtSelectedSongName.Text = string.IsNullOrEmpty(item.FolderName) ? "Untitled Song" : item.FolderName;
            TxtSelectedSongSub.Text = $"Artist: {item.Artist} | Title: {item.Title}";

            TxtSelectedStatus.Text = item.StatusText;
            BadgeStatus.Background = item.Status switch
            {
                TimingStatus.TimingIssue => new SolidColorBrush(Color.FromRgb(254, 226, 226)),
                TimingStatus.Ok => new SolidColorBrush(Color.FromRgb(209, 250, 229)),
                _ => new SolidColorBrush(Color.FromRgb(241, 245, 249))
            };
            TxtSelectedStatus.Foreground = item.Status switch
            {
                TimingStatus.TimingIssue => new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                TimingStatus.Ok => new SolidColorBrush(Color.FromRgb(5, 150, 105)),
                _ => new SolidColorBrush(Color.FromRgb(100, 116, 139))
            };

            // Update Trash Toggle Button
            BtnToggleTrash.IsEnabled = true;
            UpdateTrashToggleButton(item.IsMarkedForTrash);

            // UltraStar file parameters
            TxtGapVal.Text = $"{item.GapMs:N0} ms ({(item.GapMs / 1000.0):0.00}s)";
            TxtBpmVal.Text = $"{item.Bpm:0.##}";
            TxtFirstNoteVal.Text = $"Beat {item.FirstBeat} (Word: \"{item.FirstLyricWord}\")";
            TxtCalcVocalVal.Text = $"{item.CalculatedFirstVocalSec:0.00} seconds";

            // Online Reference parameters
            if (item.ReferenceFirstVocalSec.HasValue)
            {
                TxtRefLineVal.Text = string.IsNullOrEmpty(item.ReferenceFirstLine) ? "-" : item.ReferenceFirstLine;
                TxtRefVocalVal.Text = $"{item.ReferenceFirstVocalSec.Value:0.00} seconds";

                string sign = (item.DeltaSec ?? 0) >= 0 ? "+" : "";
                TxtDeltaVal.Text = $"{sign}{item.DeltaSec:0.00} seconds";
                TxtDeltaVal.Foreground = item.Status == TimingStatus.TimingIssue
                    ? new SolidColorBrush(Color.FromRgb(220, 38, 38))
                    : new SolidColorBrush(Color.FromRgb(5, 150, 105));
            }
            else
            {
                TxtRefLineVal.Text = "Not available";
                TxtRefVocalVal.Text = "Not available";
                TxtDeltaVal.Text = "-";
                TxtDeltaVal.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            }

            TxtDiagnosis.Text = string.IsNullOrEmpty(item.StatusMessage) ? "No diagnosis info available." : item.StatusMessage;
            BtnOpenFolder.IsEnabled = !string.IsNullOrEmpty(item.FolderPath) && Directory.Exists(item.FolderPath);
        }

        private void UpdateTrashToggleButton(bool isMarked)
        {
            if (isMarked)
            {
                TxtToggleTrash.Text = "Unmark (Keep Song)";
                IconToggleTrash.Data = (Geometry)FindResource("UndoIconGeometry");
                IconToggleTrash.Fill = new SolidColorBrush(Color.FromRgb(217, 119, 6)); // Amber
                BtnToggleTrash.Background = new SolidColorBrush(Color.FromRgb(254, 243, 199));
                BtnToggleTrash.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
                BtnToggleTrash.BorderBrush = new SolidColorBrush(Color.FromRgb(253, 230, 138));
                BtnToggleTrash.ToolTip = "Undo mark: Keep this song in library";
            }
            else
            {
                TxtToggleTrash.Text = "Mark for Trash";
                IconToggleTrash.Data = (Geometry)FindResource("TrashIconGeometry");
                IconToggleTrash.Fill = new SolidColorBrush(Color.FromRgb(220, 38, 38)); // Red
                BtnToggleTrash.Background = new SolidColorBrush(Color.FromRgb(255, 241, 242));
                BtnToggleTrash.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                BtnToggleTrash.BorderBrush = new SolidColorBrush(Color.FromRgb(254, 205, 211));
                BtnToggleTrash.ToolTip = "Mark this broken song to move to Trash folder";
            }
        }

        private void BtnToggleTrash_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null)
            {
                _selectedItem.IsMarkedForTrash = !_selectedItem.IsMarkedForTrash;
                UpdateTrashToggleButton(_selectedItem.IsMarkedForTrash);
                _songsView?.Refresh();
                UpdateFilterCounts();

                TxtStatus.Text = _selectedItem.IsMarkedForTrash
                    ? $"Marked '{_selectedItem.FolderName}' for move to trash."
                    : $"Unmarked '{_selectedItem.FolderName}' (kept in library).";
            }
        }

        private void ClearDetails()
        {
            TxtSelectedSongName.Text = "No Song Selected";
            TxtSelectedSongSub.Text = "Select a song from the list to inspect timing breakdown";
            TxtSelectedStatus.Text = "Pending";
            BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
            TxtSelectedStatus.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            TxtGapVal.Text = "-";
            TxtBpmVal.Text = "-";
            TxtFirstNoteVal.Text = "-";
            TxtCalcVocalVal.Text = "-";
            TxtRefLineVal.Text = "-";
            TxtRefVocalVal.Text = "-";
            TxtDeltaVal.Text = "-";
            TxtDiagnosis.Text = "Diagnosis details will appear here.";
            BtnOpenFolder.IsEnabled = false;
            BtnToggleTrash.IsEnabled = false;
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null && Directory.Exists(_selectedItem.FolderPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _selectedItem.FolderPath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Move Marked Songs to Trash

        private async void BtnMoveTrash_Click(object sender, RoutedEventArgs e)
        {
            string trashDir = TxtTrashFolder.Text.Trim();
            if (string.IsNullOrEmpty(trashDir) || !Directory.Exists(trashDir))
            {
                MessageBox.Show("Please select a valid destination Trash folder.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var songsToMove = _allSongs.Where(s => s.IsMarkedForTrash).ToList();
            if (songsToMove.Count == 0)
            {
                MessageBox.Show("No songs are currently marked for trash.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"Are you sure you want to move {songsToMove.Count} marked song(s) to:\n{trashDir}?",
                "Confirm Move to Trash",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            SetBusyState(true);
            ProgScan.Visibility = Visibility.Visible;
            ProgScan.Maximum = songsToMove.Count;
            ProgScan.Value = 0;

            int movedCount = 0;
            var failedSongs = new List<(string Name, string Reason)>();

            await Task.Run(() =>
            {
                for (int i = 0; i < songsToMove.Count; i++)
                {
                    var song = songsToMove[i];
                    string sourcePath = song.FolderPath;
                    string destPath = Path.Combine(trashDir, song.FolderName);

                    Dispatcher.Invoke(() =>
                    {
                        TxtStatus.Text = $"Moving ({i + 1}/{songsToMove.Count}): {song.FolderName}...";
                        ProgScan.Value = i;
                    });

                    try
                    {
                        if (!Directory.Exists(sourcePath))
                        {
                            failedSongs.Add((song.FolderName, "Source folder no longer exists."));
                            continue;
                        }

                        // Move directory (supporting cross-volume and merging)
                        MoveDirectorySafe(sourcePath, destPath);
                        movedCount++;

                        Dispatcher.Invoke(() =>
                        {
                            _allSongs.Remove(song);
                        });
                    }
                    catch (IOException ioEx)
                    {
                        failedSongs.Add((song.FolderName, $"File locked or inaccessible ({ioEx.Message}). Please make sure UltraStar is closed."));
                    }
                    catch (Exception ex)
                    {
                        failedSongs.Add((song.FolderName, ex.Message));
                    }
                }
            });

            SetBusyState(false);
            ProgScan.Visibility = Visibility.Collapsed;
            _songsView?.Refresh();
            UpdateFilterCounts();
            ClearDetails();

            string summaryMsg = $"Move to Trash finished! Successfully moved {movedCount} of {songsToMove.Count} song(s).";
            if (failedSongs.Count > 0)
            {
                summaryMsg += $"\n\nCould not move {failedSongs.Count} folder(s):\n" +
                              string.Join("\n", failedSongs.Select(f => $"• {f.Name}: {f.Reason}"));
                MessageBox.Show(summaryMsg, "Move Finished with Notes", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(summaryMsg, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            TxtStatus.Text = $"Finished moving to trash: {movedCount} moved, {failedSongs.Count} failed/locked.";
        }

        private static void MoveDirectorySafe(string sourceDir, string destinationDir)
        {
            if (!Directory.Exists(destinationDir))
            {
                try
                {
                    Directory.Move(sourceDir, destinationDir);
                    return;
                }
                catch (IOException)
                {
                    // Fallback for cross-volume moves
                }
            }

            CopyDirectoryRecursive(sourceDir, destinationDir);
            Directory.Delete(sourceDir, true);
        }

        private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
            }

            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
                CopyDirectoryRecursive(subDir, destSubDir);
            }
        }

        #endregion

        private void SetBusyState(bool isBusy)
        {
            BtnBackToMenu.IsEnabled = !isBusy;
            BtnBrowse.IsEnabled = !isBusy;
            BtnBrowseTrash.IsEnabled = !isBusy;
            BtnScan.IsEnabled = !isBusy;
            BtnMoveTrash.IsEnabled = !isBusy && _allSongs.Any(s => s.IsMarkedForTrash) && !string.IsNullOrWhiteSpace(TxtTrashFolder.Text);
            TxtScanFolder.IsEnabled = !isBusy;
            TxtTrashFolder.IsEnabled = !isBusy;
            CmbTolerance.IsEnabled = !isBusy;
            Cursor = isBusy ? Cursors.Wait : Cursors.Arrow;
        }
    }
}
