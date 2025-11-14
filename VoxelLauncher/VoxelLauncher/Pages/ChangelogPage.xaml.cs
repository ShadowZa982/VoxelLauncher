using CmlLib.Core.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VoxelLauncher.Pages
{
    public sealed partial class ChangelogPage : Page
    {
        private readonly HttpClient _httpClient = new();
        private Changelogs? _minecraftChangelogs;
        private List<string> _launcherVersions = new();
        private string? _latestTag;
        private List<GitHubRelease>? _allReleases;

        public ChangelogPage()
        {
            this.InitializeComponent();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("VoxelLauncherUpdater/1.0");
            this.Loaded += ChangelogPage_Loaded;
        }

        private async void ChangelogPage_Loaded(object sender, RoutedEventArgs e)
        {
            await Task.WhenAll(
                LoadLauncherReleasesAsync(),
                LoadMinecraftChangelogsAsync()
            );
        }

        #region === LAUNCHER CHANGELOG ===
        private async Task LoadLauncherReleasesAsync()
        {
            try
            {
                var appcastUrl = "https://github.com/ShadowZa982/VoxelLauncher/releases/latest/download/appcast.xml";
                var xmlContent = await _httpClient.GetStringAsync(appcastUrl);
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(xmlContent);
                var nsmgr = new System.Xml.XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("sparkle", "http://www.andymatuschak.org/xml-namespaces/sparkle");
                var tagNode = doc.SelectSingleNode("//sparkle:tag_name", nsmgr);
                _latestTag = tagNode?.InnerText.Trim();

                var apiUrl = "https://api.github.com/repos/ShadowZa982/VoxelLauncher/releases";
                var json = await _httpClient.GetStringAsync(apiUrl);
                _allReleases = JsonSerializer.Deserialize<List<GitHubRelease>>(json);

                var validReleases = _allReleases?
                    .Where(r => !r.Draft && !r.Prerelease)
                    .OrderByDescending(r => r.TagName, new VersionTagComparer())
                    .ToList() ?? new();

                _launcherVersions = validReleases.Select(r => r.TagName).ToList();
                LauncherVersionList.ItemsSource = _launcherVersions
                                    .Select(tag => tag == _latestTag ? $"{tag} [Latest]" : tag)
                                    .ToList();



                if (_launcherVersions.Any())
                    LauncherVersionList.SelectedIndex = 0;

            }
            catch (Exception ex)
            {
                await ShowErrorAsync("Không tải được danh sách phiên bản", ex.Message);
            }
        }

        private async void LauncherVersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LauncherVersionList.SelectedItem is string displayText)
            {
                var tag = displayText.Replace(" [Latest]", "");
                await LoadLauncherChangelogAsync(tag);
            }
        }
        private void LauncherTabButton_Click(object sender, RoutedEventArgs e)
        {
            LauncherTab.Visibility = Visibility.Visible;
            MinecraftTab.Visibility = Visibility.Collapsed;

            LauncherTabButton.Style = (Style)Resources["ActiveSourceButtonStyle"];
            MinecraftTabButton.Style = (Style)Resources["SourceButtonStyle"];
        }

        private void MinecraftTabButton_Click(object sender, RoutedEventArgs e)
        {
            LauncherTab.Visibility = Visibility.Collapsed;
            MinecraftTab.Visibility = Visibility.Visible;

            MinecraftTabButton.Style = (Style)Resources["ActiveSourceButtonStyle"];
            LauncherTabButton.Style = (Style)Resources["SourceButtonStyle"];
        }

        private async Task LoadLauncherChangelogAsync(string tag)
        {
            try
            {
                if (_allReleases == null)
                {
                    var apiUrl = "https://api.github.com/repos/ShadowZa982/VoxelLauncher/releases";
                    _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("request");
                    var json = await _httpClient.GetStringAsync(apiUrl);
                    _allReleases = JsonSerializer.Deserialize<List<GitHubRelease>>(json);
                }

                var targetRelease = _allReleases?.FirstOrDefault(r => r.TagName == tag);
                if (targetRelease == null)
                {
                    await ShowWebViewAsync(LauncherWebView, $"<h2>Không tìm thấy release cho {tag}</h2>");
                    return;
                }

                if (string.IsNullOrWhiteSpace(targetRelease.Body))
                {
                    await ShowWebViewAsync(LauncherWebView, $"<h2>Không có nội dung changelog cho {tag}</h2>");
                    return;
                }

                var html = ConvertMarkdownToHtml(targetRelease.Body, tag);
                await ShowWebViewAsync(LauncherWebView, html);
            }
            catch (Exception ex)
            {
                await ShowWebViewAsync(LauncherWebView, $"<h2>Lỗi: {ex.Message}</h2>");
            }
        }


        private string ConvertMarkdownToHtml(string markdown, string version)
        {
            var lines = markdown.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var sb = new System.Text.StringBuilder();

            sb.Append($@"
    <html>
    <head>
        <style>
            body {{
                font-family: 'Segoe UI', sans-serif;
                background: #2D2D2D;
                color: white;
                padding: 20px;
                line-height: 1.6;
            }}

            img {{
                max-width: 100%;
                max-height: 100%;
                border-radius: 8px;
                display: block;
                margin: 10px auto;
            }}

            h1 {{
                color: #22C55E;
                border-bottom: 2px solid #22C55E;
                padding-bottom: 10px;
                margin-bottom: 20px;
            }}

            h2 {{
                color: #4ADE80;
                margin: 24px 0 12px;
            }}

            h3 {{
                color: #86EFAC;
                margin: 18px 0 8px;
            }}

            ul, ol {{
                padding-left: 24px;
                margin: 12px 0;
            }}

            li {{
                margin: 6px 0;
            }}

            code {{
                background: #1E1E1E;
                padding: 2px 6px;
                border-radius: 4px;
                font-family: Consolas;
            }}

            pre {{
                background: #1E1E1E;
                padding: 12px;
                border-radius: 8px;
                overflow-x: auto;
                margin: 16px 0;
            }}

            pre code {{
                padding: 0;
                background: none;
            }}
        </style>
    </head>
    <body>
        <h1>Voxel Launcher {version}</h1>");


            bool inCodeBlock = false;
            foreach (var line in lines)
            {
                var l = line.Trim();
                if (string.IsNullOrEmpty(l)) continue;

                if (l.StartsWith("```"))
                {
                    if (inCodeBlock) sb.Append("</code></pre>");
                    else sb.Append("<pre><code>");
                    inCodeBlock = !inCodeBlock;
                    continue;
                }

                if (inCodeBlock)
                {
                    sb.Append(System.Web.HttpUtility.HtmlEncode(l) + "<br>");
                    continue;
                }

                if (l.StartsWith("### ")) sb.Append($"<h3>{EscapeHtml(l[4..])}</h3>");
                else if (l.StartsWith("## ")) sb.Append($"<h2>{EscapeHtml(l[3..])}</h2>");
                else if (l.StartsWith("- ") || l.StartsWith("* ")) sb.Append($"<li>{EscapeHtml(l[2..])}</li>");
                else if (l.StartsWith("`") && l.EndsWith("`") && l.Length > 2)
                    sb.Append($"<code>{EscapeHtml(l[1..^1])}</code>");
                else
                {
                    string formatted;

                    if (l.StartsWith("<img", StringComparison.OrdinalIgnoreCase))
                    {
                        formatted = l;
                        l = Regex.Replace(l, @"\s(width|height)=""\d+""", "", RegexOptions.IgnoreCase);

                    }
                    else
                    {
                        formatted = EscapeHtml(l);
                        formatted = Regex.Replace(formatted, @"!\[(.*?)\]\((https?://[^\s]+)\)", "<img src=\"$2\" alt=\"$1\" style='max-width:100%;border-radius:8px;margin:10px auto;display:block;'/>");
                        formatted = Regex.Replace(formatted, @"#(.+?)#", "<span style='font-size:18px;font-weight:bold;'>$1</span>");
                        formatted = Regex.Replace(formatted, @"\*\*(.+?)\*\*", "<b>$1</b>");
                        formatted = Regex.Replace(formatted, @"\*(.+?)\*", "<i>$1</i>");
                        formatted = Regex.Replace(formatted, @"__(.+?)__", "<b>$1</b>");
                        formatted = Regex.Replace(formatted, @"_(.+?)_", "<i>$1</i>");
                        formatted = Regex.Replace(formatted, @"\[(.+?)\]\((https?://[^\s]+)\)", "<a href=\"$2\" target=\"_blank\">$1</a>");
                    }
                    sb.Append($"<p>{formatted}</p>");
                }
            }
            if (inCodeBlock) sb.Append("</code></pre>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private string EscapeHtml(string text) => System.Web.HttpUtility.HtmlEncode(text);
        #endregion

        #region === MINECRAFT CHANGELOG ===
        private async Task LoadMinecraftChangelogsAsync()
        {
            try
            {
                _minecraftChangelogs = await Changelogs.GetChangelogs(_httpClient);
                var versions = _minecraftChangelogs.GetAvailableVersions();
                var sortedVersions = versions
                    .OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                MinecraftVersionList.ItemsSource = sortedVersions;
                if (sortedVersions.Any())
                    MinecraftVersionList.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("Không tải được changelog Minecraft", ex.Message);
            }
        }

        private async void MinecraftVersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MinecraftVersionList.SelectedItem is string version)
            {
                await LoadMinecraftChangelogAsync(version);
            }
        }

        private async Task LoadMinecraftChangelogAsync(string version)
        {
            if (_minecraftChangelogs == null) return;
            try
            {
                var html = await _minecraftChangelogs.GetChangelogHtml(version);
                if (string.IsNullOrEmpty(html))
                {
                    await ShowWebViewAsync(MinecraftWebView, $"<h2>Không có changelog cho {version}</h2>");
                    return;
                }

                var fullHtml = $@"
                    <html><head><style>
                        body {{ font-family: 'Segoe UI'; background: #2D2D2D; color: white; padding: 20px; line-height: 1.6; }}
                        h1, h2, h3 {{ color: #22C55E; }}
                        a {{ color: #4ADE80; text-decoration: underline; }}
                        ul, ol {{ padding-left: 24px; margin: 12px 0; }}
                        li {{ margin: 6px 0; }}
                        img {{ max-width: 100%; border-radius: 8px; }}
                    </style></head>
                    <body>
                        <h1>Minecraft Java Edition {version}</h1>
                        {html}
                    </body></html>";

                await ShowWebViewAsync(MinecraftWebView, fullHtml);
            }
            catch (Exception ex)
            {
                await ShowWebViewAsync(MinecraftWebView, $"<h2>Lỗi: {ex.Message}</h2>");
            }
        }
        #endregion

        #region === HELPER ===
        private async Task ShowWebViewAsync(WebView2 webView, string html)
        {
            await webView.EnsureCoreWebView2Async();
            webView.NavigateToString(html);
        }

        private async Task ShowErrorAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
        #endregion

        private class VersionTagComparer : IComparer<string>
        {
            public int Compare(string? x, string? y)
            {
                if (x == null || y == null) return 0;
                var vx = ParseVersion(x);
                var vy = ParseVersion(y);
                return vy.CompareTo(vx);
            }

            private Version ParseVersion(string tag)
            {
                var clean = Regex.Replace(tag, @"^v", "", RegexOptions.IgnoreCase);
                if (Version.TryParse(clean, out var v)) return v;
                return new Version(0, 0);
            }
        }
    }
    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        public bool Draft { get; set; }
        public bool Prerelease { get; set; }

        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = new();
    }


    public class GitHubAsset
    {
        public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }
}