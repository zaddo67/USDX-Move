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
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using USDX_Move.Models;
using USDX_Move.Services;

namespace USDX_Move.Views
{
    /// <summary>
    /// Interaction logic for FixBackgroundView.xaml
    /// </summary>
    public partial class FixBackgroundView : UserControl
    {
        public event EventHandler? BackRequested;

        private readonly ObservableCollection<FixBackgroundItem> _items = new();
        private readonly ICollectionView _itemsView;
        private FixBackgroundItem? _selectedItem;
        private CancellationTokenSource? _scanCts;

        public FixBackgroundView()
        {
            _itemsView = CollectionViewSource.GetDefaultView(_items);
            _itemsView.Filter = FilterItem;

            InitializeComponent();

            LstSongs.ItemsSource = _itemsView;

            _items.CollectionChanged += (s, e) => UpdateCounts();
            UpdateCounts();
        }

        private void BtnBackToMenu_Click(object sender, RoutedEventArgs e)
        {
            _scanCts?.Cancel();
            BackRequested?.Invoke(this, EventArgs.Empty);
        }

        #region Filtering

        private bool FilterItem(object obj)
        {
            if (obj is not FixBackgroundItem item) return false;

            if (!string.IsNullOrWhiteSpace(TxtFilter?.Text))
            {
                string query = TxtFilter.Text.Trim();
                return item.FolderName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       item.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       item.Title.Contains(query, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            _itemsView?.Refresh();
            UpdateCounts();
        }

        private void BtnClearFilter_Click(object sender, RoutedEventArgs e)
        {
            TxtFilter.Text = string.Empty;
        }

        private void UpdateCounts()
        {
            int total = _items.Count;
            int unfixed = _items.Count(i => !i.IsFixed);

            bool isFiltering = !string.IsNullOrWhiteSpace(TxtFilter?.Text);
            if (isFiltering)
            {
                int visible = _items.Count(FilterItem);
                TxtMissingCount.Text = $"{visible} of {total} song{(total == 1 ? "" : "s")}";
            }
            else
            {
                TxtMissingCount.Text = $"{total} song{(total == 1 ? "" : "s")}";
            }

            if (TxtFixAllBtn != null)
            {
                TxtFixAllBtn.Text = $"Fix All Missing Backgrounds ({unfixed})";
            }
            if (BtnFixAll != null)
            {
                BtnFixAll.IsEnabled = unfixed > 0;
            }
        }

        #endregion

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

            _scanCts?.Cancel();
            _scanCts = new CancellationTokenSource();
            var ct = _scanCts.Token;

            SetBusyState(true);
            ProgScan.Visibility = Visibility.Visible;
            ProgScan.IsIndeterminate = true;
            _items.Clear();
            _selectedItem = null;
            ClearDetails();
            TxtStatus.Text = "Scanning folders for songs missing #BACKGROUND tag...";

            var discovered = new List<FixBackgroundItem>();

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
                        string folderName = Path.GetFileName(dir);
                        var (txtPath, artist, title, cover, background) = SongTxtParser.ParseSongFolder(dir, folderName);

                        if (string.IsNullOrEmpty(txtPath) || !File.Exists(txtPath))
                            continue;

                        // Condition: Has #COVER tag, but missing or empty #BACKGROUND tag
                        if (!string.IsNullOrWhiteSpace(cover) && string.IsNullOrWhiteSpace(background))
                        {
                            string fullCoverPath = Path.Combine(dir, cover);
                            bool coverExists = File.Exists(fullCoverPath);

                            discovered.Add(new FixBackgroundItem
                            {
                                FolderName = folderName,
                                FolderPath = dir,
                                RelativePath = Path.GetRelativePath(rootPath, dir),
                                TxtFilePath = txtPath,
                                Artist = artist,
                                Title = title,
                                CoverFileName = cover,
                                CoverImageFullPath = coverExists ? fullCoverPath : null,
                                CurrentBackgroundTag = background
                            });
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

            foreach (var item in discovered.OrderBy(x => x.FolderName, StringComparer.CurrentCultureIgnoreCase))
            {
                _items.Add(item);
            }

            SetBusyState(false);
            ProgScan.Visibility = Visibility.Collapsed;
            ProgScan.IsIndeterminate = false;
            _itemsView?.Refresh();
            UpdateCounts();

            TxtStatus.Text = $"Scan complete. Found {_items.Count} song(s) with #COVER but missing #BACKGROUND.";
        }

        #endregion

        #region Details & Fix Actions

        private void LstSongs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstSongs.SelectedItem is FixBackgroundItem item)
            {
                _selectedItem = item;
                DisplayItemDetails(item);
            }
        }

        private void DisplayItemDetails(FixBackgroundItem item)
        {
            TxtSelectedSongName.Text = string.IsNullOrEmpty(item.FolderName) ? "Untitled Song" : item.FolderName;
            TxtSelectedSongSub.Text = $"Artist: {item.Artist} | Title: {item.Title}";

            TxtCoverTagVal.Text = item.CoverFileName + (item.CoverImageFullPath != null ? " (File exists ✓)" : " (File missing on disk)");
            TxtBackgroundTagVal.Text = item.IsFixed ? $"{item.CoverFileName} (Fixed ✓)" : "[Missing / Blank]";
            TxtFeedback.Text = "";

            // Load Cover Image Preview safely without locking the file
            if (!string.IsNullOrEmpty(item.CoverImageFullPath) && File.Exists(item.CoverImageFullPath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(item.CoverImageFullPath);
                    bitmap.EndInit();
                    bitmap.Freeze();

                    ImgCoverPreview.Source = bitmap;
                    ImgCoverPreview.Visibility = Visibility.Visible;
                    TxtNoImageNotice.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    ImgCoverPreview.Source = null;
                    ImgCoverPreview.Visibility = Visibility.Collapsed;
                    TxtNoImageNotice.Text = "Could not decode cover image";
                    TxtNoImageNotice.Visibility = Visibility.Visible;
                }
            }
            else
            {
                ImgCoverPreview.Source = null;
                ImgCoverPreview.Visibility = Visibility.Collapsed;
                TxtNoImageNotice.Text = "No cover image file found on disk";
                TxtNoImageNotice.Visibility = Visibility.Visible;
            }

            BtnFixSelected.IsEnabled = !item.IsFixed;
            TxtFixSelectedBtn.Text = item.IsFixed ? "Background Already Set" : $"Set Background = {item.CoverFileName}";
        }

        private void ClearDetails()
        {
            TxtSelectedSongName.Text = "No Song Selected";
            TxtSelectedSongSub.Text = "Select a song from the list to preview";
            TxtCoverTagVal.Text = "-";
            TxtBackgroundTagVal.Text = "-";
            ImgCoverPreview.Source = null;
            ImgCoverPreview.Visibility = Visibility.Collapsed;
            TxtNoImageNotice.Visibility = Visibility.Collapsed;
            BtnFixSelected.IsEnabled = false;
            TxtFeedback.Text = "";
        }

        private void BtnFixSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;

            try
            {
                SongTxtParser.UpdateBackgroundTag(_selectedItem.TxtFilePath, _selectedItem.CoverFileName);
                _selectedItem.IsFixed = true;
                _selectedItem.CurrentBackgroundTag = _selectedItem.CoverFileName;

                DisplayItemDetails(_selectedItem);
                UpdateCounts();

                TxtFeedback.Text = $"✓ Updated #BACKGROUND to '{_selectedItem.CoverFileName}'!";
                TxtStatus.Text = $"Fixed background for '{_selectedItem.FolderName}'.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update .txt file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnFixAll_Click(object sender, RoutedEventArgs e)
        {
            var unfixed = _items.Where(i => !i.IsFixed).ToList();
            if (unfixed.Count == 0) return;

            var confirm = MessageBox.Show(
                $"Are you sure you want to update #BACKGROUND for all {unfixed.Count} detected songs to match their #COVER image?",
                "Confirm Fix All",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            SetBusyState(true);
            ProgScan.Visibility = Visibility.Visible;
            ProgScan.IsIndeterminate = false;
            ProgScan.Maximum = unfixed.Count;
            ProgScan.Value = 0;

            int fixedCount = 0;
            var errors = new List<(string Name, string Reason)>();

            await Task.Run(() =>
            {
                for (int i = 0; i < unfixed.Count; i++)
                {
                    var item = unfixed[i];
                    Dispatcher.Invoke(() =>
                    {
                        ProgScan.Value = i + 1;
                        TxtStatus.Text = $"Updating ({i + 1}/{unfixed.Count}): {item.FolderName}...";
                    });

                    try
                    {
                        SongTxtParser.UpdateBackgroundTag(item.TxtFilePath, item.CoverFileName);
                        item.IsFixed = true;
                        item.CurrentBackgroundTag = item.CoverFileName;
                        fixedCount++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add((item.FolderName, ex.Message));
                    }
                }
            });

            SetBusyState(false);
            ProgScan.Visibility = Visibility.Collapsed;
            UpdateCounts();
            if (_selectedItem != null) DisplayItemDetails(_selectedItem);

            string msg = $"Batch update finished! Successfully updated {fixedCount} song(s).";
            if (errors.Count > 0)
            {
                msg += $"\n\nErrors encountered ({errors.Count}):\n" +
                       string.Join("\n", errors.Select(e => $"• {e.Name}: {e.Reason}"));
                MessageBox.Show(msg, "Finished with Notes", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(msg, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            TxtStatus.Text = $"Completed batch background update: {fixedCount} updated, {errors.Count} errors.";
        }

        #endregion

        private void SetBusyState(bool isBusy)
        {
            BtnBackToMenu.IsEnabled = !isBusy;
            BtnBrowse.IsEnabled = !isBusy;
            BtnScan.IsEnabled = !isBusy;
            BtnFixAll.IsEnabled = !isBusy && _items.Any(i => !i.IsFixed);
            BtnFixSelected.IsEnabled = !isBusy && _selectedItem != null && !_selectedItem.IsFixed;
            TxtScanFolder.IsEnabled = !isBusy;
            Cursor = isBusy ? Cursors.Wait : Cursors.Arrow;
        }
    }
}
