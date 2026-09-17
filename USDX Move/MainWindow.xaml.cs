using System;
using System.Windows;
using USDX_Move.Views;

namespace USDX_Move
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainMenuView _mainMenuView;
        private MoveFoldersView? _moveFoldersView;
        private FindImageView? _findImageView;
        private CheckTimingView? _checkTimingView;
        private FixBackgroundView? _fixBackgroundView;
        private DedupeSongsView? _dedupeSongsView;
        private PlaylistEditorView? _playlistEditorView;

        public MainWindow()
        {
            InitializeComponent();

            _mainMenuView = new MainMenuView();
            _mainMenuView.ToolSelected += OnToolSelected;

            ShowMainMenu();
        }

        private void ShowMainMenu()
        {
            MainContent.Content = _mainMenuView;
            Title = "USDX Tools Suite - Main Menu";
        }

        private void OnToolSelected(object? sender, string toolName)
        {
            switch (toolName)
            {
                case "MoveFolders":
                    if (_moveFoldersView == null)
                    {
                        _moveFoldersView = new MoveFoldersView();
                        _moveFoldersView.BackRequested += (s, e) => ShowMainMenu();
                    }
                    MainContent.Content = _moveFoldersView;
                    Title = "USDX Tools Suite - Move Folders";
                    break;

                case "FindImage":
                    if (_findImageView == null)
                    {
                        _findImageView = new FindImageView();
                        _findImageView.BackRequested += (s, e) => ShowMainMenu();
                    }
                    MainContent.Content = _findImageView;
                    Title = "USDX Tools Suite - Find Image";
                    break;

                case "CheckTiming":
                    if (_checkTimingView == null)
                    {
                        _checkTimingView = new CheckTimingView();
                        _checkTimingView.BackRequested += (s, e) => ShowMainMenu();
                    }
                    MainContent.Content = _checkTimingView;
                    Title = "USDX Tools Suite - Check Timing";
                    break;

                case "FixBackground":
                    if (_fixBackgroundView == null)
                    {
                        _fixBackgroundView = new FixBackgroundView();
                        _fixBackgroundView.BackRequested += (s, e) => ShowMainMenu();
                    }
                    MainContent.Content = _fixBackgroundView;
                    Title = "USDX Tools Suite - Fix Background";
                    break;

                case "DedupeSongs":
                    if (_dedupeSongsView == null)
                    {
                        _dedupeSongsView = new DedupeSongsView();
                        _dedupeSongsView.BackRequested += (s, e) => ShowMainMenu();
                    }
                    MainContent.Content = _dedupeSongsView;
                    Title = "USDX Tools Suite - Dedupe Songs";
                    break;

                case "PlaylistEditor":
                    if (_playlistEditorView == null)
                    {
                        _playlistEditorView = new PlaylistEditorView();
                        _playlistEditorView.BackRequested += (s, e) => ShowMainMenu();
                    }
                    MainContent.Content = _playlistEditorView;
                    Title = "USDX Tools Suite - Playlist Editor";
                    break;
            }
        }
    }
}
