using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using USDX_Move.Models;

namespace USDX_Move.Services
{
    public class TimingCheckService
    {
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        static TimingCheckService()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("USDXTimingChecker/1.0 (https://github.com/ultrastar)");
        }

        /// <summary>
        /// Parses the timing parameters (#GAP, #BPM, first note) from a song's .txt file.
        /// </summary>
        public TimingCheckItem ParseSongTiming(string folderPath, string relativePath)
        {
            string folderName = Path.GetFileName(folderPath);
            var (txtPath, artist, title, _, _) = SongTxtParser.ParseSongFolder(folderPath, folderName);

            var item = new TimingCheckItem
            {
                FolderName = folderName,
                FolderPath = folderPath,
                RelativePath = relativePath,
                TxtFilePath = txtPath,
                Artist = artist,
                Title = title
            };

            if (string.IsNullOrEmpty(txtPath) || !File.Exists(txtPath))
            {
                item.Status = TimingStatus.Error;
                item.StatusMessage = "No .txt file found.";
                return item;
            }

            try
            {
                var lines = File.ReadAllLines(txtPath);
                double gapMs = 0;
                double bpm = 0;
                int firstBeat = -1;
                string firstWord = "";

                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();
                    if (line.StartsWith("#GAP:", StringComparison.OrdinalIgnoreCase))
                    {
                        string val = line[5..].Trim().Replace(',', '.');
                        if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedGap))
                        {
                            gapMs = parsedGap;
                        }
                    }
                    else if (line.StartsWith("#BPM:", StringComparison.OrdinalIgnoreCase))
                    {
                        string val = line[5..].Trim().Replace(',', '.');
                        if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedBpm))
                        {
                            bpm = parsedBpm;
                        }
                    }
                    else if (firstBeat == -1 && (line.StartsWith(":") || line.StartsWith("*") || line.StartsWith("F") || line.StartsWith("R") || line.StartsWith("G")))
                    {
                        // Note format: : <start_beat> <duration> <pitch> <syllable>
                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4 && int.TryParse(parts[1], out int beat))
                        {
                            firstBeat = beat;
                            firstWord = string.Join(' ', parts.Skip(4));
                        }
                    }
                }

                item.GapMs = gapMs;
                item.Bpm = bpm;
                item.FirstBeat = Math.Max(0, firstBeat);
                item.FirstLyricWord = firstWord;

                // UltraStar calculation:
                // Note beat duration in seconds = 60.0 / (BPM * 4.0)
                double beatDurationSec = bpm > 0 ? 60.0 / (bpm * 4.0) : 0;
                item.CalculatedFirstVocalSec = (gapMs / 1000.0) + (item.FirstBeat * beatDurationSec);
            }
            catch (Exception ex)
            {
                item.Status = TimingStatus.Error;
                item.StatusMessage = $"Error parsing timing: {ex.Message}";
            }

            return item;
        }

        /// <summary>
        /// Queries online synced lyrics (LRCLIB) and evaluates timing discrepancy.
        /// </summary>
        public async Task EvaluateTimingOnlineAsync(TimingCheckItem item, double toleranceSec, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(item.Artist) && string.IsNullOrWhiteSpace(item.Title))
            {
                item.Status = TimingStatus.NoReferenceFound;
                item.StatusMessage = "Missing Artist and Title metadata.";
                return;
            }

            try
            {
                var (refSec, refLine) = await FetchFirstLyricTimestampAsync(item.Artist, item.Title, cancellationToken);

                if (!refSec.HasValue)
                {
                    item.Status = TimingStatus.NoReferenceFound;
                    item.StatusMessage = "No synchronized lyrics found online.";
                    return;
                }

                item.ReferenceFirstVocalSec = refSec.Value;
                item.ReferenceFirstLine = refLine;

                double delta = item.CalculatedFirstVocalSec - refSec.Value;
                item.DeltaSec = delta;

                if (Math.Abs(delta) > toleranceSec)
                {
                    item.Status = TimingStatus.TimingIssue;
                    string desc = delta > 0 
                        ? $"Starts ~{delta:0.1}s too late compared to reference" 
                        : $"Starts ~{Math.Abs(delta):0.1}s too early compared to reference";
                    item.StatusMessage = desc;
                }
                else
                {
                    item.Status = TimingStatus.Ok;
                    item.StatusMessage = $"Timing matches online reference (within {Math.Abs(delta):0.2}s).";
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                item.Status = TimingStatus.Error;
                item.StatusMessage = $"Online check error: {ex.Message}";
            }
        }

        private async Task<(double? TimestampSec, string LineText)> FetchFirstLyricTimestampAsync(string artist, string title, CancellationToken cancellationToken)
        {
            string url = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(title)}";

            string? syncedLyrics = null;

            try
            {
                var response = await _httpClient.GetStringAsync(url, cancellationToken);
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("syncedLyrics", out var syncedElem))
                {
                    syncedLyrics = syncedElem.GetString();
                }
            }
            catch
            {
                // Fallback to search query if exact get fails
                try
                {
                    string searchUrl = $"https://lrclib.net/api/search?q={Uri.EscapeDataString($"{artist} {title}")}";
                    var response = await _httpClient.GetStringAsync(searchUrl, cancellationToken);
                    using var doc = JsonDocument.Parse(response);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in doc.RootElement.EnumerateArray())
                        {
                            if (elem.TryGetProperty("syncedLyrics", out var s) && !string.IsNullOrEmpty(s.GetString()))
                            {
                                syncedLyrics = s.GetString();
                                break;
                            }
                        }
                    }
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(syncedLyrics))
                return (null, "");

            // Parse synced lyrics line by line: [00:15.23] Lyrics...
            var lines = syncedLyrics.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var lrcRegex = new Regex(@"^\[(\d{1,2}):(\d{2}(?:\.\d{1,3})?)\]\s*(.*)$");

            foreach (var line in lines)
            {
                var match = lrcRegex.Match(line.Trim());
                if (match.Success)
                {
                    string minStr = match.Groups[1].Value;
                    string secStr = match.Groups[2].Value;
                    string text = match.Groups[3].Value.Trim();

                    // Skip empty lines or instrumental header tags
                    if (string.IsNullOrWhiteSpace(text) || text.StartsWith("♪") || text.Equals("[instrumental]", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (double.TryParse(minStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double min) &&
                        double.TryParse(secStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double sec))
                    {
                        double totalSec = (min * 60.0) + sec;
                        return (totalSec, $"[{match.Groups[1].Value}:{match.Groups[2].Value}] {text}");
                    }
                }
            }

            return (null, "");
        }
    }
}
