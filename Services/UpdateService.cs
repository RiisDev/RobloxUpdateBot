using System.Globalization;
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
        private readonly ulong _guildId = ulong.Parse(Environment.GetEnvironmentVariable("GUILD_ID") ?? "0");
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

        private DateTime ParseDateTime(string input) => DateTime.ParseExact(input, ["MMM d, yyyy", "MMM dd, yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None);

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

        private async Task IosVersionWatcherNew(string statusKey, string storeUrl)
        {
            Status? currentStatus = _databaseService.GetStatus(statusKey);
            if (currentStatus == null) return;

            int breakIndex = currentStatus.Version.IndexOf('|');
            string lastVersion = breakIndex <= 0 ? currentStatus.Version : currentStatus.Version[..breakIndex];
            DateTime lastDate = breakIndex <= 0 ? DateTime.MinValue : ParseDateTime(lastVersion[(breakIndex + 1)..]);

            HttpRequestMessage request = new(HttpMethod.Get, storeUrl);
            HttpResponseMessage response = await _client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;

            string content = await response.Content.ReadAsStringAsync();
            
            Match versionMatch = Regex.Match(content, @"Version\s+(\d{1,4}\.\d{1,4}\.\d{1,5})");
            Match dateMatch = Regex.Match(content, @"<time[^>]*>(.*?)<\/time>");

            if (!dateMatch.Success) return;
            if (!versionMatch.Success) return;

            string currentUpdate = versionMatch.Groups[1].Value;
            DateTime currentDate = ParseDateTime(dateMatch.Groups[1].Value);

            if (lastVersion == currentUpdate) return;
            if (lastDate >= currentDate) return;

            Status newStatus = currentStatus with
            {
                Version = $"{currentUpdate}|{currentDate}",
                Updated = false
            };

            _databaseService.UpdateStatus(newStatus);
            await UpdateDetected(newStatus, lastVersion);
        }


        private async Task AndroidVersionWatcher(string statusKey, string storeUrl)
        {
            Status? currentStatus = _databaseService.GetStatus(statusKey);
            if (currentStatus == null) return;

            int breakIndex = currentStatus.Version.IndexOf('|');
            string lastVersion = breakIndex <= 0 ? currentStatus.Version : currentStatus.Version[..breakIndex];
            DateTime lastDate = breakIndex <= 0 ? DateTime.MinValue : ParseDateTime(lastVersion[(breakIndex+1)..]);

            HttpRequestMessage request = new(HttpMethod.Get, storeUrl);
            HttpResponseMessage response = await _client.SendAsync(request);

            if (!response.IsSuccessStatusCode) return;

            string content = await response.Content.ReadAsStringAsync();

            Match versionMatch = Regex.Match(content, @"\[\""(\d{1,4}\.\d{1,4}\.\d{1,5})\""\]");
            Match dateMatch = Regex.Match(content, @"Updated\s*on<\/div>\s*<div[^>]*>([^<]+)<\/div>");

            if (!dateMatch.Success) return;
            if (!versionMatch.Success) return;

            string currentUpdate = versionMatch.Groups[1].Value;
            DateTime currentDate = ParseDateTime(dateMatch.Groups[1].Value);

            if (lastVersion == currentUpdate) return;
            if (lastDate >= currentDate) return;

            Status newStatus = currentStatus with
            {
                Version = $"{currentUpdate}|{currentDate}",
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
                    IosVersionWatcherNew("IOS", "https://apps.apple.com/us/app/roblox/id431946152?uo=4"),
                    IosVersionWatcherNew("IOS-VNG", "https://apps.apple.com/vn/app/roblox-vn/id6474715805?uo=4"),
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
            IGuildChannel? channel = (await _discordService.Rest.GetGuildChannelsAsync(_guildId)).FirstOrDefault(x=> x.Id == client.ChannelId);
            if (channel is null) return;

            await channel.ModifyAsync(x => x.Name = _databaseService.GetChannel(channel.Id).ChannelUpdatedFalseText);
            
            channel = (await _discordService.Rest.GetGuildChannelsAsync(_guildId)).FirstOrDefault(x => x.Id == _databaseService.GetLog());
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
}
