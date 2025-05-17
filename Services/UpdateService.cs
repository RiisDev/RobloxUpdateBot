using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;

namespace RobloxUpdateBot.Services
{
    public class UpdateService
    {
        private readonly GatewayClient _discordService;
        private readonly DatabaseService _databaseService;

        private readonly HttpClient _client = new(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            AllowAutoRedirect = true,
        });

        private readonly System.Timers.Timer _timer = new();
        public UpdateService(IHost host)
        {
            _discordService = host.Services.GetRequiredService<GatewayClient>();
            _databaseService = host.Services.GetRequiredService<DatabaseService>();

            _client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:138.0) Gecko/20100101 Firefox/138.0");
            _client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            _timer.Interval = int.Parse(Environment.GetEnvironmentVariable("RECHECK_MS") ?? "1800000");
            _timer.Elapsed += async (_, _) => await RunAllWatchersAsync();
            _timer.AutoReset = true;
            _timer.Enabled = true;
            _timer.Start();

            _ = RunAllWatchersAsync();
        }

        private async Task WindowsVersionWatcher()
        {
            Status? currentStatus = _databaseService.GetStatus("Windows");
            if (currentStatus == null) return;

            string lastVersion = currentStatus.Version;

            HttpRequestMessage request = new(HttpMethod.Get, "https://clientsettings.roblox.com/v2/client-version/WindowsPlayer/channel/LIVE");
            HttpResponseMessage response = await _client.SendAsync(request);

            if (!response.IsSuccessStatusCode) return;

            RobloxVersion? robloxVersion = await response.Content.ReadFromJsonAsync<RobloxVersion>();
            if (robloxVersion is null) return;
            if (robloxVersion.ClientVersionUpload == currentStatus.Version) return;

            Status newStatus = currentStatus with
            {
                Version = robloxVersion.ClientVersionUpload,
                Updated = false
            };

            _databaseService.UpdateStatus(newStatus);

            await UpdateDetected(newStatus, lastVersion);
        }

        private async Task MacVersionWatcher()
        {
            Status? currentStatus = _databaseService.GetStatus("Mac");
            if (currentStatus == null) return;

            string lastVersion = currentStatus.Version;

            HttpRequestMessage request = new(HttpMethod.Get, "https://clientsettings.roblox.com/v2/client-version/MacPlayer/channel/LIVE");
            HttpResponseMessage response = await _client.SendAsync(request);

            if (!response.IsSuccessStatusCode) return;

            RobloxVersion? robloxVersion = await response.Content.ReadFromJsonAsync<RobloxVersion>();
            if (robloxVersion is null) return;
            if (robloxVersion.ClientVersionUpload == currentStatus.Version) return;

            Status newStatus = currentStatus with
            {
                Version = robloxVersion.ClientVersionUpload,
                Updated = false
            };

            _databaseService.UpdateStatus(newStatus);

            await UpdateDetected(newStatus, lastVersion);
        }
        private async Task IosVersionWatcher(string statusKey, string storeUrl)
        {
            Status? currentStatus = _databaseService.GetStatus(statusKey);
            if (currentStatus == null) return;
            string lastUpdate = currentStatus.Version;
            HttpRequestMessage request = new(HttpMethod.Get, storeUrl);
            HttpResponseMessage response = await _client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;
            
            IosResult? iosResult = await response.Content.ReadFromJsonAsync<IosResult>();
            if (iosResult is null || iosResult.Results.Count == 0) return;
            if (iosResult.Results[0].Version == lastUpdate) return;

            Status newStatus = currentStatus with
            {
                Version = iosResult.Results[0].Version,
                Updated = false
            };

            _databaseService.UpdateStatus(newStatus);
            await UpdateDetected(newStatus, lastUpdate);
        }
        
        private async Task AndroidVersionWatcher(string statusKey, string storeUrl)
        {
            Status? currentStatus = _databaseService.GetStatus(statusKey);
            if (currentStatus == null) return;

            string lastVersion = currentStatus.Version;

            HttpRequestMessage request = new(HttpMethod.Get, storeUrl);
            HttpResponseMessage response = await _client.SendAsync(request);

            if (!response.IsSuccessStatusCode) return;

            string content = await response.Content.ReadAsStringAsync();

            Match versionMatch = Regex.Match(content, @"\[\""(\d{1,4}\.\d{1,4}\.\d{1,5})\""\]");

            if (!versionMatch.Success) return;

            string currentUpdate = versionMatch.Groups[1].Value;
            
            Status newStatus = currentStatus with
            {
                Version = currentUpdate,
                Updated = false
            };

            _databaseService.UpdateStatus(newStatus);
            await UpdateDetected(newStatus, lastVersion);
        }


        private async Task RunAllWatchersAsync()
        {
            try
            {
                Task[] tasks =
                [
                    WindowsVersionWatcher(),
                    MacVersionWatcher(),
                    IosVersionWatcher("IOS", "https://itunes.apple.com/search?term=id431946152&country=us&entity=software"),
                    IosVersionWatcher("IOS-VNG", "https://itunes.apple.com/search?term=id6474715805&country=vn&entity=software"),
                    AndroidVersionWatcher("Android", "https://play.google.com/store/apps/details?id=com.roblox.client&hl=en"),
                    AndroidVersionWatcher("Android-VNG", "https://play.google.com/store/apps/details?id=com.roblox.client.vnggames&hl=en")
                ];

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RunAllWatchersAsync: {ex.Message}");
            }
        }


        private async Task UpdateDetected(Status client, string oldVersion)
        {
            IGuildChannel? channel = (await _discordService.Rest.GetGuildChannelsAsync(1372898533274816533)).FirstOrDefault(x=> x.Id == client.ChannelId);
            if (channel is null) return;

            await channel.ModifyAsync(x => x.Name = _databaseService.GetChannel(channel.Id)?.ChannelUpdatedFalseText ?? "N/A");
            
            channel = (await _discordService.Rest.GetGuildChannelsAsync(1372898533274816533)).FirstOrDefault(x => x.Id == _databaseService.GetLog());
            if (channel is null) return;

            await _discordService.Rest.SendMessageAsync(channel.Id, new MessageProperties
            {
                Content = "",
                Embeds = [
                    new EmbedProperties
                    {
                        Title = $"{client.Client} Update Detected",
                        Description = $"Version: ``{client.Version}``\nOld Version: ``{oldVersion}``",
                        Color = new Color(255,0,0),
                        Footer = new EmbedFooterProperties
                        {
                            Text = $"{Environment.GetEnvironmentVariable("EXPLOIT_NAME")} Update Bot | Written by RiisDev @ GitHub"
                        },
                        Timestamp = DateTime.UtcNow
                    }
                ]
            });
        }
    }

    public record RobloxVersion(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("clientVersionUpload")] string ClientVersionUpload,
        [property: JsonPropertyName("bootstrapperVersion")] string BootstrapperVersion
    );

    public record IosSubResult(
        [property: JsonPropertyName("isGameCenterEnabled")] bool? IsGameCenterEnabled,
        [property: JsonPropertyName("features")] IReadOnlyList<string> Features,
        [property: JsonPropertyName("advisories")] IReadOnlyList<string> Advisories,
        [property: JsonPropertyName("supportedDevices")] IReadOnlyList<string> SupportedDevices,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("artistViewUrl")] string ArtistViewUrl,
        [property: JsonPropertyName("artworkUrl60")] string ArtworkUrl60,
        [property: JsonPropertyName("artworkUrl100")] string ArtworkUrl100,
        [property: JsonPropertyName("screenshotUrls")] IReadOnlyList<string> ScreenshotUrls,
        [property: JsonPropertyName("ipadScreenshotUrls")] IReadOnlyList<string> IpadScreenshotUrls,
        [property: JsonPropertyName("appletvScreenshotUrls")] IReadOnlyList<object> AppletvScreenshotUrls,
        [property: JsonPropertyName("artworkUrl512")] string ArtworkUrl512,
        [property: JsonPropertyName("artistId")] int? ArtistId,
        [property: JsonPropertyName("artistName")] string ArtistName,
        [property: JsonPropertyName("genres")] IReadOnlyList<string> Genres,
        [property: JsonPropertyName("price")] double? Price,
        [property: JsonPropertyName("trackId")] long? TrackId,
        [property: JsonPropertyName("trackName")] string TrackName,
        [property: JsonPropertyName("bundleId")] string BundleId,
        [property: JsonPropertyName("isVppDeviceBasedLicensingEnabled")] bool? IsVppDeviceBasedLicensingEnabled,
        [property: JsonPropertyName("releaseDate")] DateTime? ReleaseDate,
        [property: JsonPropertyName("primaryGenreName")] string PrimaryGenreName,
        [property: JsonPropertyName("primaryGenreId")] int? PrimaryGenreId,
        [property: JsonPropertyName("sellerName")] string SellerName,
        [property: JsonPropertyName("genreIds")] IReadOnlyList<string> GenreIds,
        [property: JsonPropertyName("currentVersionReleaseDate")] DateTime? CurrentVersionReleaseDate,
        [property: JsonPropertyName("releaseNotes")] string ReleaseNotes,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("wrapperType")] string WrapperType,
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("averageUserRating")] double? AverageUserRating,
        [property: JsonPropertyName("trackCensoredName")] string TrackCensoredName,
        [property: JsonPropertyName("trackViewUrl")] string TrackViewUrl,
        [property: JsonPropertyName("contentAdvisoryRating")] string ContentAdvisoryRating,
        [property: JsonPropertyName("minimumOsVersion")] string MinimumOsVersion,
        [property: JsonPropertyName("averageUserRatingForCurrentVersion")] double? AverageUserRatingForCurrentVersion,
        [property: JsonPropertyName("sellerUrl")] string SellerUrl,
        [property: JsonPropertyName("languageCodesISO2A")] IReadOnlyList<string> LanguageCodesIso2A,
        [property: JsonPropertyName("fileSizeBytes")] string FileSizeBytes,
        [property: JsonPropertyName("formattedPrice")] string FormattedPrice,
        [property: JsonPropertyName("userRatingCountForCurrentVersion")] int? UserRatingCountForCurrentVersion,
        [property: JsonPropertyName("trackContentRating")] string TrackContentRating,
        [property: JsonPropertyName("userRatingCount")] int? UserRatingCount
    );

    public record IosResult(
        [property: JsonPropertyName("resultCount")] int? ResultCount,
        [property: JsonPropertyName("results")] IReadOnlyList<IosSubResult> Results
    );


}
