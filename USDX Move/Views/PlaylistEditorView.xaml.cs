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
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using USDX_Move.Models;
using USDX_Move.Services;

namespace USDX_Move.Views
{
    public partial class PlaylistEditorView : UserControl
    {
        public event EventHandler? BackRequested;

        private readonly PlaylistSettingsService _settingsService = new();
        private readonly PlaylistFileService _playlistFiles = new();
        private readonly SongLibraryService _songLibrary = new();
        private readonly SongPreviewService _preview = new();
        private readonly ObservableCollection<PlaylistSongItem> _songs = new();
        private readonly ObservableCollection<PlaylistSongItem> _playlistSongs = new();
        private readonly ObservableCollection<PlaylistDefinition> _playlists = new();
        private readonly ICollectionView _songsView;
        private readonly ICollectionView _playlistView;
        private PlaylistDefinition? _currentPlaylist;
        private PlaylistSongItem? _selectedSong;
        private bool _loading;

        public PlaylistEditorView()
        {
            _songsView = CollectionViewSource.GetDefaultView(_songs);
            _songsView.Filter = MatchesSongSearch;
            _playlistView = CollectionViewSource.GetDefaultView(_playlistSongs);
            _playlistView.Filter = MatchesPlaylistSearch;
            InitializeComponent();
            LstSongs.ItemsSource = _songsView;
            LstPlaylist.ItemsSource = _playlistView;
            CmbPlaylists.ItemsSource = _playlists;

            var settings = _settingsService.Load();
            TxtSongsFolder.Text = string.IsNullOrWhiteSpace(settings.SongsFolder) ? @"E:\Games\Ultrastar\songs" : settings.SongsFolder;
            TxtPlaylistsFolder.Text = string.IsNullOrWhiteSpace(settings.PlaylistsFolder) ? @"E:\Games\Ultrastar\playlists" : settings.PlaylistsFolder;
        }

        private async void BtnLoadFolders_Click(object sender, RoutedEventArgs e)
        {
            SaveFolderSettings();
            await LoadSongsAsync();
            LoadPlaylists();
        }

        private async Task LoadSongsAsync()
        {
            string root = TxtSongsFolder.Text.Trim();
            if (!Directory.Exists(root)) { ShowWarning("Choose a valid songs folder."); return; }
            TxtStatus.Text = "Scanning song library…";
            var scanned = await Task.Run(() => _songLibrary.Scan(root));
            _songs.Clear();
            foreach (var song in scanned) _songs.Add(song);
            UpdatePlaylistMarkers();
            _songsView.Refresh();
            TxtStatus.Text = $"Loaded {_songs.Count} songs.";
        }

        private void LoadPlaylists()
        {
            string folder = TxtPlaylistsFolder.Text.Trim();
            if (!Directory.Exists(folder)) { ShowWarning("Choose a valid playlists folder."); return; }
            _loading = true;
            _playlists.Clear();
            foreach (var playlist in _playlistFiles.GetPlaylists(folder)) _playlists.Add(playlist);
            _loading = false;
            TxtStatus.Text = $"Loaded {_playlists.Count} playlist(s).";
        }

        private void CmbPlaylists_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || CmbPlaylists.SelectedItem is not PlaylistDefinition playlist) return;
            _currentPlaylist = playlist;
            TxtPlaylistName.Text = playlist.Name;
            _playlistSongs.Clear();
            foreach (string entry in playlist.SongEntries)
            {
                var song = _songs.FirstOrDefault(s => string.Equals(s.PlaylistEntry, entry, StringComparison.OrdinalIgnoreCase));
                if (song != null) _playlistSongs.Add(song);
            }
            UpdatePlaylistMarkers();
            TxtStatus.Text = $"Loaded '{playlist.Name}' ({_playlistSongs.Count} available song(s)).";
        }

        private void BtnNew_Click(object sender, RoutedEventArgs e)
        {
            _currentPlaylist = null;
            CmbPlaylists.SelectedItem = null;
            TxtPlaylistName.Text = string.Empty;
            _playlistSongs.Clear();
            UpdatePlaylistMarkers();
            TxtPlaylistName.Focus();
            TxtStatus.Text = "Enter a playlist name, add songs, then Save.";
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string folder = TxtPlaylistsFolder.Text.Trim();
            try
            {
                string path = _playlistFiles.Save(folder, TxtPlaylistName.Text, _playlistSongs);
                LoadPlaylists();
                _currentPlaylist = _playlists.FirstOrDefault(p => string.Equals(p.FilePath, path, StringComparison.OrdinalIgnoreCase));
                CmbPlaylists.SelectedItem = _currentPlaylist;
                TxtStatus.Text = $"Saved '{TxtPlaylistName.Text.Trim()}'.";
            }
            catch (Exception ex) { ShowWarning(ex.Message); }
        }

        private void BtnDeletePlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPlaylist == null) { ShowWarning("Select a saved playlist to delete."); return; }
            if (MessageBox.Show($"Delete playlist '{_currentPlaylist.Name}'?", "Delete Playlist", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _playlistFiles.Delete(_currentPlaylist.FilePath);
            BtnNew_Click(sender, e);
            LoadPlaylists();
            TxtStatus.Text = "Playlist deleted.";
        }

        private void LstSongs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedSong = LstSongs.SelectedItem as PlaylistSongItem;
            TxtSelectedSong.Text = _selectedSong?.DisplayName ?? "Select a song";
            TxtAudioStatus.Text = _selectedSong == null ? string.Empty : File.Exists(_selectedSong.AudioPath) ? "Audio preview available" : "Audio file not found";
            ImgCover.Source = _selectedSong != null && File.Exists(_selectedSong.CoverPath) ? new BitmapImage(new Uri(_selectedSong.CoverPath)) : null;
        }

        private void BtnQuickAdd_Click(object sender, RoutedEventArgs e) => AddSong((sender as FrameworkElement)?.DataContext as PlaylistSongItem);
        private void BtnAddSelected_Click(object sender, RoutedEventArgs e) => AddSong(_selectedSong);
        private void AddSong(PlaylistSongItem? song)
        {
            if (song == null) { ShowWarning("Select a song to add."); return; }
            if (_playlistSongs.Any(s => string.Equals(s.PlaylistEntry, song.PlaylistEntry, StringComparison.OrdinalIgnoreCase))) { TxtStatus.Text = "That song is already in the playlist."; return; }
            _playlistSongs.Add(song);
            song.IsInPlaylist = true;
            _playlistView.Refresh();
            LstPlaylist.ScrollIntoView(song);
            TxtStatus.Text = $"Added {song.DisplayName}.";
        }

        private void BtnDeleteSong_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not PlaylistSongItem song) return;
            _playlistSongs.Remove(song);
            song.IsInPlaylist = false;
            _playlistView.Refresh();
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSong == null) { ShowWarning("Select a song first."); return; }
            try { _preview.Play(_selectedSong.AudioPath); TxtStatus.Text = $"Playing {_selectedSong.DisplayName}."; }
            catch (Exception ex) { ShowWarning(ex.Message); }
        }
        private void BtnStop_Click(object sender, RoutedEventArgs e) { _preview.Stop(); TxtStatus.Text = "Playback stopped."; }

        private void BtnBrowseSongs_Click(object sender, RoutedEventArgs e) => BrowseFolder(TxtSongsFolder, "Select Songs Folder");
        private void BtnBrowsePlaylists_Click(object sender, RoutedEventArgs e) => BrowseFolder(TxtPlaylistsFolder, "Select Playlists Folder");
        private static void BrowseFolder(TextBox target, string title)
        {
            var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
            if (Directory.Exists(target.Text)) dialog.InitialDirectory = target.Text;
            if (dialog.ShowDialog() == true) target.Text = dialog.FolderName;
        }

        private void Folder_TextChanged(object sender, TextChangedEventArgs e) { if (!_loading) SaveFolderSettings(); }
        private void SaveFolderSettings() => _settingsService.Save(new PlaylistSettings { SongsFolder = TxtSongsFolder.Text.Trim(), PlaylistsFolder = TxtPlaylistsFolder.Text.Trim() });
        private bool MatchesSongSearch(object item) => Matches(item as PlaylistSongItem, TxtSongSearch?.Text);
        private bool MatchesPlaylistSearch(object item) => Matches(item as PlaylistSongItem, TxtPlaylistSearch?.Text);
        private static bool Matches(PlaylistSongItem? song, string? search) => song != null && (string.IsNullOrWhiteSpace(search) || song.Artist.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) || song.Title.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
        private void TxtSongSearch_TextChanged(object sender, TextChangedEventArgs e) => _songsView?.Refresh();
        private void TxtPlaylistSearch_TextChanged(object sender, TextChangedEventArgs e) => _playlistView?.Refresh();
        private void BtnClearSongSearch_Click(object sender, RoutedEventArgs e) => TxtSongSearch.Text = string.Empty;
        private void BtnClearPlaylistSearch_Click(object sender, RoutedEventArgs e) => TxtPlaylistSearch.Text = string.Empty;
        private void UpdatePlaylistMarkers() { var entries = _playlistSongs.Select(s => s.PlaylistEntry).ToHashSet(StringComparer.OrdinalIgnoreCase); foreach (var song in _songs) song.IsInPlaylist = entries.Contains(song.PlaylistEntry); TxtPlaylistCount.Text = $"{_playlistSongs.Count} song{(_playlistSongs.Count == 1 ? "" : "s")}"; }
        private void BtnBack_Click(object sender, RoutedEventArgs e) { _preview.Stop(); BackRequested?.Invoke(this, EventArgs.Empty); }
        private static void ShowWarning(string message) => MessageBox.Show(message, "Playlist Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
