using System;
using System.Windows;
using System.Windows.Controls;

namespace USDX_Move.Views
{
    /// <summary>
    /// Interaction logic for MainMenuView.xaml
    /// </summary>
    public partial class MainMenuView : UserControl
    {
        public event EventHandler<string>? ToolSelected;

        public MainMenuView()
        {
            InitializeComponent();
        }

        private void BtnMoveFolders_Click(object sender, RoutedEventArgs e)
        {
            ToolSelected?.Invoke(this, "MoveFolders");
        }

        private void BtnFindImage_Click(object sender, RoutedEventArgs e)
        {
            ToolSelected?.Invoke(this, "FindImage");
        }

        private void BtnCheckTiming_Click(object sender, RoutedEventArgs e)
        {
            ToolSelected?.Invoke(this, "CheckTiming");
        }

        private void BtnFixBackground_Click(object sender, RoutedEventArgs e)
        {
            ToolSelected?.Invoke(this, "FixBackground");
        }

        private void BtnDedupeSongs_Click(object sender, RoutedEventArgs e)
        {
            ToolSelected?.Invoke(this, "DedupeSongs");
        }

        private void BtnPlaylistEditor_Click(object sender, RoutedEventArgs e)
        {
            ToolSelected?.Invoke(this, "PlaylistEditor");
        }
    }
}
