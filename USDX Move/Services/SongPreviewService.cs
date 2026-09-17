using System;
using System.IO;
using System.Windows.Media;

namespace USDX_Move.Services
{
    public sealed class SongPreviewService
    {
        private readonly MediaPlayer _player = new();

        public void Play(string audioPath)
        {
            if (!File.Exists(audioPath)) throw new FileNotFoundException("The song's #MP3 file could not be found.", audioPath);
            _player.Open(new Uri(audioPath));
            _player.Play();
        }

        public void Stop() => _player.Stop();

        public void SkipForward(TimeSpan amount)
        {
            var targetPosition = _player.Position + amount;
            _player.Position = targetPosition < TimeSpan.Zero ? TimeSpan.Zero : targetPosition;
        }
    }
}
