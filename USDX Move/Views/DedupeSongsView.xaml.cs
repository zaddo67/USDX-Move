using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
    /// Interaction logic for DedupeSongsView.xaml
    /// </summary>
    public partial class DedupeSongsView : UserControl
    {
        public event EventHandler? BackRequested;

        private readonly ObservableCollection<DuplicateSongGroup> _groups = new();
        private readonly ICollectionView _groupsView;
        private readonly DedupeService _dedupeService = new();

        private DuplicateSongGroup? _selectedGroup;
        private CancellationTokenSource? _scanCts;

        public DedupeSongsView()
        {
            _groupsView = CollectionViewSource.GetDefaultView(_groups);
            _groupsView.Filter = FilterGroup;

            InitializeComponent();

            LstGroups.ItemsSource = _groupsView;

            _groups.CollectionChanged += (s, e) => UpdateCounts();
            UpdateCounts();
        }

        private void BtnBackToMenu_Click(object sender, RoutedEventArgs e)
        {
            _scanCts?.Cancel();
            BackRequested?.Invoke(this, EventArgs.Empty);
        }

        #region Filtering

        private bool FilterGroup(object obj)
        {
            if (obj is not DuplicateSongGroup group) return false;

            if (!string.IsNullOrWhiteSpace(TxtFilter?.Text))
            {
                string query = TxtFilter.Text.Trim();
                return group.DisplayArtist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       group.DisplayTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       group.Versions.Any(v => v.FolderName.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            return true;
        }

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            _groupsView?.Refresh();
            UpdateCounts();
        }

        private void BtnClearFilter_Click(object sender, RoutedEventArgs e)
        {
            TxtFilter.Text = string.Empty;
        }

        private void UpdateCounts()
        {
            int totalGroups = _groups.Count;
            int totalTrash = _groups.Sum(g => g.TrashCount);

            bool isFiltering = !string.IsNullOrWhiteSpace(TxtFilter?.Text);
            if (isFiltering)
            {
                int visible = _groups.Count(FilterGroup);
                TxtGroupCount.Text = $"{visible} of {totalGroups} duplicate group{(totalGroups == 1 ? "" : "s")}";
            }
            else
            {
                TxtGroupCount.Text = $"{totalGroups} duplicate group{(totalGroups == 1 ? "" : "s")}";
            }

            if (TxtMoveDuplicatesBtn != null)
            {
                TxtMoveDuplicatesBtn.Text = $"Move Duplicates ({totalTrash})";
            }
            if (BtnMoveDuplicates != null)
            {
                BtnMoveDuplicates.IsEnabled = totalTrash > 0 && !string.IsNullOrWhiteSpace(TxtTrashFolder?.Text);
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
                UpdateCounts();
            }
        }

        private void TxtTrashFolder_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCounts();
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
            _groups.Clear();
            _selectedGroup = null;
            ClearDetails();
            TxtStatus.Text = "Scanning folders and grouping duplicate songs by Artist/Title...";

            List<DuplicateSongGroup> discovered = new();

            await Task.Run(() =>
            {
                discovered = _dedupeService.ScanAndFindDuplicates(rootPath, ct);
            }, ct);

            if (ct.IsCancellationRequested)
            {
                SetBusyState(false);
                return;
            }

            foreach (var g in discovered)
            {
                _groups.Add(g);
            }

            SetBusyState(false);
            ProgScan.Visibility = Visibility.Collapsed;
            ProgScan.IsIndeterminate = false;
            _groupsView?.Refresh();
            UpdateCounts();

            int totalDuplicates = _groups.Sum(g => g.TrashCount);
            TxtStatus.Text = $"Scan complete. Found {_groups.Count} duplicate group(s) ({totalDuplicates} duplicate folders staged for move).";
        }

        #endregion

        #region Group Selection & Version Controls

        private void LstGroups_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstGroups.SelectedItem is DuplicateSongGroup group)
            {
                _selectedGroup = group;
                DisplayGroupDetails(group);
            }
        }

        private void DisplayGroupDetails(DuplicateSongGroup group)
        {
            TxtSelectedGroupTitle.Text = $"{group.DisplayArtist} - {group.DisplayTitle}";
            TxtSelectedGroupSub.Text = $"{group.TotalCopies} total copies found across your library | {group.TrashCount} marked for trash";
            LstVersions.ItemsSource = group.Versions;
        }

        private void ClearDetails()
        {
            TxtSelectedGroupTitle.Text = "No Group Selected";
            TxtSelectedGroupSub.Text = "Select a duplicate song group from the left to inspect versions";
            LstVersions.ItemsSource = null;
        }

        private void BtnSetKeepVersion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is DuplicateSongVersion version && _selectedGroup != null)
            {
                _selectedGroup.SetKeep(version);
                DisplayGroupDetails(_selectedGroup);
                UpdateCounts();
                _groupsView?.Refresh();

                TxtStatus.Text = $"Set '{version.FolderName}' as the version to KEEP.";
            }
        }

        private void BtnOpenVersionFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is DuplicateSongVersion version)
            {
                if (Directory.Exists(version.FolderPath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = version.FolderPath,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Could not open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        #endregion

        #region Move Duplicates to Trash

        private async void BtnMoveDuplicates_Click(object sender, RoutedEventArgs e)
        {
            string trashDir = TxtTrashFolder.Text.Trim();
            if (string.IsNullOrEmpty(trashDir) || !Directory.Exists(trashDir))
            {
                MessageBox.Show("Please select a valid destination Trash folder.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var duplicateVersionsToMove = _groups
                .SelectMany(g => g.Versions.Where(v => v.IsTrash).Select(v => (Group: g, Version: v)))
                .ToList();

            if (duplicateVersionsToMove.Count == 0)
            {
                MessageBox.Show("No duplicate songs are currently staged for trash.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"Are you sure you want to move {duplicateVersionsToMove.Count} duplicate song folder(s) to:\n{trashDir}?\n\nThe preferred/kept versions will remain in your library.",
                "Confirm Move Duplicates to Trash",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            SetBusyState(true);
            ProgScan.Visibility = Visibility.Visible;
            ProgScan.IsIndeterminate = false;
            ProgScan.Maximum = duplicateVersionsToMove.Count;
            ProgScan.Value = 0;

            int movedCount = 0;
            var failedFolders = new List<(string Name, string Reason)>();

            await Task.Run(() =>
            {
                for (int i = 0; i < duplicateVersionsToMove.Count; i++)
                {
                    var item = duplicateVersionsToMove[i];
                    string sourcePath = item.Version.FolderPath;
                    string destPath = Path.Combine(trashDir, item.Version.FolderName);

                    Dispatcher.Invoke(() =>
                    {
                        TxtStatus.Text = $"Moving duplicate ({i + 1}/{duplicateVersionsToMove.Count}): {item.Version.FolderName}...";
                        ProgScan.Value = i;
                    });

                    try
                    {
                        if (!Directory.Exists(sourcePath))
                        {
                            failedFolders.Add((item.Version.FolderName, "Source folder no longer exists."));
                            continue;
                        }

                        MoveDirectorySafe(sourcePath, destPath);
                        movedCount++;

                        Dispatcher.Invoke(() =>
                        {
                            item.Group.Versions.Remove(item.Version);
                            if (item.Group.Versions.Count <= 1)
                            {
                                _groups.Remove(item.Group);
                            }
                        });
                    }
                    catch (IOException ioEx)
                    {
                        failedFolders.Add((item.Version.FolderName, $"File locked: {ioEx.Message}"));
                    }
                    catch (Exception ex)
                    {
                        failedFolders.Add((item.Version.FolderName, ex.Message));
                    }
                }
            });

            SetBusyState(false);
            ProgScan.Visibility = Visibility.Collapsed;
            _groupsView?.Refresh();
            UpdateCounts();
            if (_selectedGroup != null)
            {
                if (_groups.Contains(_selectedGroup))
                {
                    DisplayGroupDetails(_selectedGroup);
                }
                else
                {
                    ClearDetails();
                }
            }

            string summaryMsg = $"Move to Trash finished! Successfully moved {movedCount} of {duplicateVersionsToMove.Count} duplicate song(s).";
            if (failedFolders.Count > 0)
            {
                summaryMsg += $"\n\nCould not move {failedFolders.Count} folder(s):\n" +
                              string.Join("\n", failedFolders.Select(f => $"• {f.Name}: {f.Reason}"));
                MessageBox.Show(summaryMsg, "Move Finished with Notes", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(summaryMsg, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            TxtStatus.Text = $"Completed move to trash: {movedCount} moved, {failedFolders.Count} failed/locked.";
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
            BtnMoveDuplicates.IsEnabled = !isBusy && _groups.Any(g => g.TrashCount > 0) && !string.IsNullOrWhiteSpace(TxtTrashFolder.Text);
            TxtScanFolder.IsEnabled = !isBusy;
            TxtTrashFolder.IsEnabled = !isBusy;
            Cursor = isBusy ? Cursors.Wait : Cursors.Arrow;
        }
    }
}
