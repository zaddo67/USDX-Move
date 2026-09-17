using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using USDX_Move.Models;

namespace USDX_Move.Views
{
    /// <summary>
    /// Interaction logic for MoveFoldersView.xaml
    /// </summary>
    public partial class MoveFoldersView : UserControl
    {
        public event EventHandler? BackRequested;

        private readonly ObservableCollection<FolderItem> _sourceFolders = new();
        private readonly ObservableCollection<FolderItem> _selectedFolders = new();
        private readonly ICollectionView _sourceView;

        public MoveFoldersView()
        {
            InitializeComponent();

            _sourceView = CollectionViewSource.GetDefaultView(_sourceFolders);
            _sourceView.Filter = FilterSourceFolder;

            LstSourceFolders.ItemsSource = _sourceView;
            LstSelectedFolders.ItemsSource = _selectedFolders;

            _sourceFolders.CollectionChanged += (s, e) => UpdateCounts();
            _selectedFolders.CollectionChanged += (s, e) => UpdateCounts();

            UpdateCounts();
        }

        private void BtnBackToMenu_Click(object sender, RoutedEventArgs e)
        {
            BackRequested?.Invoke(this, EventArgs.Empty);
        }

        private bool FilterSourceFolder(object item)
        {
            if (string.IsNullOrWhiteSpace(TxtFilter?.Text))
                return true;

            if (item is FolderItem folderItem)
            {
                return folderItem.Name.Contains(TxtFilter.Text.Trim(), StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            _sourceView.Refresh();
            UpdateCounts();
        }

        private void BtnClearFilter_Click(object sender, RoutedEventArgs e)
        {
            TxtFilter.Text = string.Empty;
        }

        private void UpdateCounts()
        {
            int totalSource = _sourceFolders.Count;
            bool isFiltering = !string.IsNullOrWhiteSpace(TxtFilter?.Text);

            if (isFiltering)
            {
                int visibleSourceCount = _sourceFolders.Count(FilterSourceFolder);
                TxtSourceCount.Text = $"{visibleSourceCount} of {totalSource} folder{(totalSource == 1 ? "" : "s")}";
            }
            else
            {
                TxtSourceCount.Text = $"{totalSource} folder{(totalSource == 1 ? "" : "s")}";
            }

            TxtSelectedCount.Text = $"{_selectedFolders.Count} folder{(_selectedFolders.Count == 1 ? "" : "s")}";
            BtnMove.IsEnabled = _selectedFolders.Count > 0 && !string.IsNullOrWhiteSpace(TxtToFolder.Text);
            BtnClearSelected.IsEnabled = _selectedFolders.Count > 0;
            BtnAdd.IsEnabled = _sourceFolders.Count > 0;
            BtnRemove.IsEnabled = _selectedFolders.Count > 0;
        }

        #region Folder Selection & Loading

        private void BtnBrowseFrom_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Source (From) Folder",
                Multiselect = false
            };

            if (!string.IsNullOrWhiteSpace(TxtFromFolder.Text) && Directory.Exists(TxtFromFolder.Text))
            {
                dialog.InitialDirectory = TxtFromFolder.Text;
            }

            if (dialog.ShowDialog() == true)
            {
                TxtFromFolder.Text = dialog.FolderName;
                LoadSourceFolders(dialog.FolderName);
            }
        }

        private void BtnBrowseTo_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Destination (To) Folder",
                Multiselect = false
            };

            if (!string.IsNullOrWhiteSpace(TxtToFolder.Text) && Directory.Exists(TxtToFolder.Text))
            {
                dialog.InitialDirectory = TxtToFolder.Text;
            }

            if (dialog.ShowDialog() == true)
            {
                TxtToFolder.Text = dialog.FolderName;
                UpdateCounts();
            }
        }

        private void TxtFromFolder_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Directory.Exists(TxtFromFolder.Text))
            {
                LoadSourceFolders(TxtFromFolder.Text);
            }
            else
            {
                _sourceFolders.Clear();
                _selectedFolders.Clear();
                TxtStatus.Text = "Specified source folder does not exist.";
            }
        }

        private void TxtToFolder_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCounts();
        }

        private void LoadSourceFolders(string path)
        {
            try
            {
                _sourceFolders.Clear();
                _selectedFolders.Clear();

                if (!Directory.Exists(path))
                {
                    TxtStatus.Text = "Source directory not found.";
                    return;
                }

                var enumOptions = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.System
                };

                var dirs = Directory.GetDirectories(path, "*", enumOptions)
                                    .Select(d => new FolderItem
                                    {
                                        Name = Path.GetFileName(d),
                                        RelativePath = Path.GetRelativePath(path, d),
                                        FullPath = d
                                    })
                                    .Where(f => !string.IsNullOrWhiteSpace(f.Name))
                                    .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                                    .ToList();

                foreach (var dir in dirs)
                {
                    _sourceFolders.Add(dir);
                }

                _sourceView.Refresh();
                TxtStatus.Text = $"Loaded {dirs.Count} folder(s) from {path}";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Error reading directory: {ex.Message}";
                MessageBox.Show($"Error reading directory: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Transfer & Selection Controls

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            AddSelectedToMoveList();
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            RemoveSelectedFromMoveList();
        }

        private void LstSourceFolders_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            AddSelectedToMoveList();
        }

        private void LstSelectedFolders_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            RemoveSelectedFromMoveList();
        }

        private void AddSelectedToMoveList()
        {
            var selected = LstSourceFolders.SelectedItems.Cast<FolderItem>().ToList();
            if (selected.Count == 0) return;

            foreach (var item in selected)
            {
                _sourceFolders.Remove(item);
                if (!_selectedFolders.Contains(item))
                {
                    InsertSorted(_selectedFolders, item);
                }
            }
        }

        private void RemoveSelectedFromMoveList()
        {
            var selected = LstSelectedFolders.SelectedItems.Cast<FolderItem>().ToList();
            if (selected.Count == 0) return;

            foreach (var item in selected)
            {
                _selectedFolders.Remove(item);
                if (!_sourceFolders.Contains(item))
                {
                    InsertSorted(_sourceFolders, item);
                }
            }
        }

        private void BtnClearSelected_Click(object sender, RoutedEventArgs e)
        {
            var itemsToRestore = _selectedFolders.ToList();
            _selectedFolders.Clear();

            foreach (var item in itemsToRestore)
            {
                if (!_sourceFolders.Contains(item))
                {
                    InsertSorted(_sourceFolders, item);
                }
            }

            TxtStatus.Text = "Cleared selection.";
        }

        private static void InsertSorted(ObservableCollection<FolderItem> collection, FolderItem item)
        {
            int index = 0;
            while (index < collection.Count && string.Compare(collection[index].Name, item.Name, StringComparison.CurrentCultureIgnoreCase) < 0)
            {
                index++;
            }
            collection.Insert(index, item);
        }

        #endregion

        #region Move Execution

        private async void BtnMove_Click(object sender, RoutedEventArgs e)
        {
            string fromDir = TxtFromFolder.Text.Trim();
            string toDir = TxtToFolder.Text.Trim();

            if (string.IsNullOrEmpty(fromDir) || !Directory.Exists(fromDir))
            {
                MessageBox.Show("Please select a valid 'From' folder.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(toDir) || !Directory.Exists(toDir))
            {
                MessageBox.Show("Please select a valid 'To' folder.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.Equals(Path.GetFullPath(fromDir), Path.GetFullPath(toDir), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Source and Destination folders cannot be the same.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var foldersToMove = _selectedFolders.ToList();
            if (foldersToMove.Count == 0)
            {
                MessageBox.Show("No folders selected to move.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"Are you sure you want to move {foldersToMove.Count} folder(s) to:\n{toDir}?",
                "Confirm Move",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            SetBusyState(true);
            ProgMove.Visibility = Visibility.Visible;
            ProgMove.Maximum = foldersToMove.Count;
            ProgMove.Value = 0;

            int movedCount = 0;
            var failedFolders = new List<(string Folder, string Reason)>();
            bool? applyToAllConflicts = null;

            await Task.Run(() =>
            {
                for (int i = 0; i < foldersToMove.Count; i++)
                {
                    FolderItem folderItem = foldersToMove[i];
                    string sourcePath = folderItem.FullPath;
                    string destPath = Path.Combine(toDir, folderItem.Name);

                    Dispatcher.Invoke(() =>
                    {
                        TxtStatus.Text = $"Moving ({i + 1}/{foldersToMove.Count}): {folderItem.Name}...";
                        ProgMove.Value = i;
                    });

                    try
                    {
                        if (!Directory.Exists(sourcePath))
                        {
                            failedFolders.Add((folderItem.Name, "Source folder no longer exists."));
                            continue;
                        }

                        if (Directory.Exists(destPath))
                        {
                            bool overwrite = false;
                            if (applyToAllConflicts.HasValue)
                            {
                                overwrite = applyToAllConflicts.Value;
                            }
                            else
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    var result = MessageBox.Show(
                                        $"The destination folder already exists:\n{destPath}\n\nDo you want to merge/overwrite existing files?",
                                        "Folder Conflict",
                                        MessageBoxButton.YesNoCancel,
                                        MessageBoxImage.Warning);

                                    if (result == MessageBoxResult.Yes)
                                    {
                                        overwrite = true;
                                    }
                                    else if (result == MessageBoxResult.No)
                                    {
                                        overwrite = false;
                                    }
                                    else
                                    {
                                        applyToAllConflicts = false;
                                        overwrite = false;
                                    }
                                });
                            }

                            if (!overwrite)
                            {
                                failedFolders.Add((folderItem.Name, "Destination folder already exists (Skipped)."));
                                continue;
                            }
                        }

                        MoveDirectory(sourcePath, destPath);

                        movedCount++;
                        Dispatcher.Invoke(() =>
                        {
                            _selectedFolders.Remove(folderItem);
                        });
                    }
                    catch (Exception ex)
                    {
                        failedFolders.Add((folderItem.Name, ex.Message));
                    }
                }
            });

            SetBusyState(false);
            ProgMove.Visibility = Visibility.Collapsed;

            string summaryMsg = $"Move completed! Successfully moved {movedCount} of {foldersToMove.Count} folder(s).";
            if (failedFolders.Count > 0)
            {
                summaryMsg += $"\n\nIssues encountered ({failedFolders.Count}):\n" +
                              string.Join("\n", failedFolders.Select(f => $"• {f.Folder}: {f.Reason}"));
                MessageBox.Show(summaryMsg, "Move Finished with Notes", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(summaryMsg, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            TxtStatus.Text = $"Finished: {movedCount} moved, {failedFolders.Count} skipped/failed.";
        }

        private static void MoveDirectory(string sourceDir, string destinationDir)
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
                    // Fallback for cross-volume / permissions
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

        private void SetBusyState(bool isBusy)
        {
            BtnBackToMenu.IsEnabled = !isBusy;
            BtnMove.IsEnabled = !isBusy;
            BtnClearSelected.IsEnabled = !isBusy;
            BtnBrowseFrom.IsEnabled = !isBusy;
            BtnBrowseTo.IsEnabled = !isBusy;
            BtnAdd.IsEnabled = !isBusy;
            BtnRemove.IsEnabled = !isBusy;
            LstSourceFolders.IsEnabled = !isBusy;
            LstSelectedFolders.IsEnabled = !isBusy;
            TxtFromFolder.IsEnabled = !isBusy;
            TxtToFolder.IsEnabled = !isBusy;
            TxtFilter.IsEnabled = !isBusy;
            Cursor = isBusy ? Cursors.Wait : Cursors.Arrow;
        }

        #endregion
    }
}
