using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using USDX_Move.Models;

namespace USDX_Move.Services
{
    public class CoverArtService
    {
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        static CoverArtService()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("USDXCoverFinder/1.0 (https://github.com/ultrastar)");
        }

        public async Task<List<CoverArtCandidate>> SearchCoverArtAsync(string artist, string title, CancellationToken cancellationToken = default)
        {
            var candidates = new List<CoverArtCandidate>();
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string query = $"{artist} {title}".Trim();
            if (string.IsNullOrWhiteSpace(query)) return candidates;

            // 1. Query iTunes Search API (very fast, high quality album art)
            try
            {
                var itunesResults = await QueryITunesAsync(query, cancellationToken);
                foreach (var item in itunesResults)
                {
                    if (seenUrls.Add(item.ImageUrl))
                    {
                        candidates.Add(item);
                    }
                }
            }
            catch { }

            // 2. Query Deezer API (excellent alternative catalogue)
            if (candidates.Count < 3)
            {
                try
                {
                    var deezerResults = await QueryDeezerAsync(artist, title, query, cancellationToken);
                    foreach (var item in deezerResults)
                    {
                        if (seenUrls.Add(item.ImageUrl))
                        {
                            candidates.Add(item);
                        }
                    }
                }
                catch { }
            }

            // Limit to top 4 diverse candidates
            return candidates.Take(4).ToList();
        }

        private async Task<List<CoverArtCandidate>> QueryITunesAsync(string query, CancellationToken cancellationToken)
        {
            var list = new List<CoverArtCandidate>();
            string url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query)}&entity=song&limit=6";

            var response = await _httpClient.GetStringAsync(url, cancellationToken);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in results.EnumerateArray())
                {
                    if (item.TryGetProperty("artworkUrl100", out var artElem))
                    {
                        string? art100 = artElem.GetString();
                        if (!string.IsNullOrEmpty(art100))
                        {
                            // Upgrade resolution to 600x600 or 1000x1000
                            string highResUrl = art100.Replace("100x100bb.jpg", "600x600bb.jpg");
                            string trackName = item.TryGetProperty("trackName", out var t) ? t.GetString() ?? "" : "";
                            string artistName = item.TryGetProperty("artistName", out var a) ? a.GetString() ?? "" : "";

                            list.Add(new CoverArtCandidate
                            {
                                ImageUrl = highResUrl,
                                Source = "iTunes / Apple Music",
                                Resolution = "600 x 600"
                            });
                        }
                    }
                }
            }

            return list;
        }

        private async Task<List<CoverArtCandidate>> QueryDeezerAsync(string artist, string title, string fallbackQuery, CancellationToken cancellationToken)
        {
            var list = new List<CoverArtCandidate>();
            string url;
            if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(title))
            {
                url = $"https://api.deezer.com/search?q={Uri.EscapeDataString($"artist:\"{artist}\" track:\"{title}\"")}&limit=4";
            }
            else
            {
                url = $"https://api.deezer.com/search?q={Uri.EscapeDataString(fallbackQuery)}&limit=4";
            }

            var response = await _httpClient.GetStringAsync(url, cancellationToken);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("album", out var album))
                    {
                        string? coverUrl = null;
                        if (album.TryGetProperty("cover_xl", out var xl)) coverUrl = xl.GetString();
                        if (string.IsNullOrEmpty(coverUrl) && album.TryGetProperty("cover_big", out var big)) coverUrl = big.GetString();
                        if (string.IsNullOrEmpty(coverUrl) && album.TryGetProperty("cover_medium", out var med)) coverUrl = med.GetString();

                        if (!string.IsNullOrEmpty(coverUrl))
                        {
                            list.Add(new CoverArtCandidate
                            {
                                ImageUrl = coverUrl,
                                Source = "Deezer",
                                Resolution = "1000 x 1000"
                            });
                        }
                    }
                }
            }

            return list;
        }

        public async Task DownloadImageAsync(string imageUrl, string destinationPath, CancellationToken cancellationToken = default)
        {
            var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl, cancellationToken);
            await File.WriteAllBytesAsync(destinationPath, imageBytes, cancellationToken);
        }
    }
}
