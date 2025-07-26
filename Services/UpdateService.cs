using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace RobloxUpdateBot.Services
{
    public class UpdateService
    {
        private static readonly Lock LogLock = new();

        private static void Log(string message, [CallerMemberName] string caller = "")
        {
            lock (LogLock)
            {
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [{caller}] {message}");
            }
        }

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
            Log($"Bound Guild: {_guildId}");

            Log("Building services...");
            _discordService = host.Services.GetRequiredService<GatewayClient>();
            _databaseService = host.Services.GetRequiredService<DatabaseService>();

            Log("Building HttpClient...");
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:138.0) Gecko/20100101 Firefox/138.0");
            _client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            Log($"Building timer: {Environment.GetEnvironmentVariable("RECHECK_MS")}ms");
            _timer.Interval = int.Parse(Environment.GetEnvironmentVariable("RECHECK_MS") ?? "1800000");
            _timer.Elapsed += async (_, _) => await RunAllWatchersAsync();
            _timer.AutoReset = true;
            _timer.Enabled = true;
            _timer.Start();
            
            Log("Starting watchers...");
            _ = RunAllWatchersAsync();
        }

        private static DateOnly ParseDateTime(string input)
        {
            Log("Parsing Date Input");

            if (string.IsNullOrWhiteSpace(input))
            {
                Log("Input is null or empty, returning default DateOnly");
                return DateOnly.MinValue;
            }

            string[] formats = [
                "MM/dd/yyyy HH:mm:ss",

                "dd MMM, yyyy HH:mm:ss",
                "d MMM, yyyy HH:mm:ss",
                "MMM d, yyyy HH:mm:ss",
                "MMM dd, yyyy HH:mm:ss",
                "dd MMM yyyy HH:mm:ss",
                "d MMM yyyy HH:mm:ss",
                "MMM d yyyy HH:mm:ss",
                "MMM dd yyyy HH:mm:ss",

                "MM/dd/yyyy",

                "dd MMM, yyyy",
                "d MMM, yyyy",
                "MMM d, yyyy",
                "MMM dd, yyyy",
                "dd MMM yyyy",
                "d MMM yyyy",
                "MMM d yyyy",
                "MMM dd yyyy"
            ];

            // Keep allowing for old datetime usage in-case using old database
            if (DateTime.TryParseExact(input, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dateTime))
            {
                DateOnly dateOnly = DateOnly.FromDateTime(dateTime);
                Log($"Parsed Date: {dateOnly}");
                return dateOnly;
            }

            Log("Failed to parse input, returning default DateOnly");
            return DateOnly.MinValue;
        }

        private async Task DesktopVersionWatcher(string platform, string url)
        {
            Log($"Checking {platform} version...");
            Status? currentStatus = _databaseService.GetStatus(platform);
            if (currentStatus == null)
            {
                Log($"No status found for {platform}, skipping watcher.");
                return;
            }

            string lastVersion = currentStatus.Version;
            Log($"{platform} last version: {lastVersion}");

            HttpRequestMessage request = new(HttpMethod.Get, url);
            HttpResponseMessage response = await _client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Log($"Failed to fetch {platform} version from {url}. Status code: {response.StatusCode}");
                return;
            }

            RobloxVersion? robloxVersion = await response.Content.ReadFromJsonAsync<RobloxVersion>();
            if (robloxVersion is null)
            {
                Log($"Failed to deserialize RobloxVersion for {platform} from {url}.");
                return;
            }
            if (robloxVersion.ClientVersionUpload == currentStatus.Version)
            {
                Log($"No update detected for {platform}. Current version: {robloxVersion.ClientVersionUpload}");
                return;
            }

            Status newStatus = currentStatus with
            {
                Version = robloxVersion.ClientVersionUpload,
                Updated = false
            };

            Log($"Update detected for {platform}. New version: {robloxVersion.ClientVersionUpload}");
            _databaseService.UpdateStatus(newStatus);
            await UpdateDetected(newStatus, lastVersion);
        }

        private async Task MobileVersionWatcher(string statusKey, string storeUrl, [StringSyntax(StringSyntaxAttribute.Regex)] string versionPattern, [StringSyntax(StringSyntaxAttribute.Regex)] string datePattern)
        {
            Log($"Checking {statusKey} version from {storeUrl}...");
            Status? currentStatus = _databaseService.GetStatus(statusKey);
            if (currentStatus == null)
            {
                Log($"No status found for {statusKey}, skipping watcher.");
                return;
            }


            int breakIndex = currentStatus.Version.IndexOf('|');
            string lastVersion = breakIndex <= 0 ? currentStatus.Version : currentStatus.Version[..breakIndex];
            DateOnly lastDate = breakIndex <= 0 ? DateOnly.MinValue : ParseDateTime(currentStatus.Version[(breakIndex + 1)..]);
            Log($"{statusKey} last version: {lastVersion}");
            Log($"{statusKey} last update date: {lastDate}");


            HttpRequestMessage request = new(HttpMethod.Get, storeUrl);
            HttpResponseMessage response = await _client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Log($"Failed to fetch {statusKey} version from {storeUrl}. Status code: {response.StatusCode}");
                return;
            }

            string content = await response.Content.ReadAsStringAsync();

            Match versionMatch = Regex.Match(content, versionPattern);
            Match dateMatch = Regex.Match(content, datePattern);

            if (!versionMatch.Success || !dateMatch.Success)
            {
                Log($"Failed to find version or date in {statusKey} content from {storeUrl}. Version match: {versionMatch.Success} Date match: {dateMatch.Success}");
                return;
            }

            string currentUpdate = versionMatch.Groups[1].Value;
            DateOnly currentDate = ParseDateTime(dateMatch.Groups[1].Value);
            Log($"{statusKey} current version: {currentUpdate}");
            Log($"{statusKey} current update date: {currentDate}");

            if (lastVersion == currentUpdate || lastDate >= currentDate)
            {
                Log($"No update detected for {statusKey}. Current version: {currentUpdate}, Last version: {lastVersion}, Current date: {currentDate}, Last date: {lastDate}");
                return;
            }

            Status newStatus = currentStatus with
            {
                Version = $"{currentUpdate}|{currentDate}",
                Updated = false
            };

            Log($"Update detected for {statusKey}. New version: {currentUpdate}, New date: {currentDate}");
            _databaseService.UpdateStatus(newStatus);
            await UpdateDetected(newStatus, lastVersion);
        }

        private async Task RunAllWatchersAsync()
        {
            try
            {
                Task[] tasks =
                [
                    DesktopVersionWatcher("Windows", "https://clientsettings.roblox.com/v2/client-version/WindowsPlayer/channel/LIVE"),
                    DesktopVersionWatcher("Mac", "https://clientsettings.roblox.com/v2/client-version/MacPlayer/channel/LIVE"),

                    MobileVersionWatcher(
                        statusKey: "IOS", 
                        storeUrl: "https://apps.apple.com/us/app/roblox/id431946152?uo=4", 
                        versionPattern: @"Version\s+(\d{1,4}\.\d{1,4}\.\d{1,5})", 
                        datePattern: @"<time[^>]*>(.*?)<\/time>"
                    ),
                    MobileVersionWatcher(
                        statusKey: "IOS-VNG", 
                        storeUrl: "https://apps.apple.com/vn/app/roblox-vn/id6474715805?uo=4", 
                        versionPattern: @"Version\s+(\d{1,4}\.\d{1,4}\.\d{1,5})", 
                        datePattern: @"<time[^>]*>(.*?)<\/time>"
                    ),

                    MobileVersionWatcher(
                        statusKey: "Android", 
                        storeUrl: "https://play.google.com/store/apps/details?id=com.roblox.client&hl=en", 
                        versionPattern: @"\[\""(\d{1,4}\.\d{1,4}\.\d{1,5})\""\]", 
                        datePattern: @"Updated\s*on<\/div>\s*<div[^>]*>([^<]+)<\/div>"
                    ),
                    MobileVersionWatcher(
                        statusKey: "Android-VNG", 
                        storeUrl: "https://play.google.com/store/apps/details?id=com.roblox.client.vnggames&hl=en", 
                        versionPattern: @"\[\""(\d{1,4}\.\d{1,4}\.\d{1,5})\""\]", 
                        datePattern: @"Updated\s*on<\/div>\s*<div[^>]*>([^<]+)<\/div>"
                    )
                ];

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                Log($"Exception in RunAllWatchersAsync: {ex}");
            }
        }

        private async Task UpdateDetected(Status client, string oldVersion)
        {
            Log($"Sending {client.Client} update message...");
            IGuildChannel? channel;

            if (client.ChannelId != 0)
            {
                channel = (await _discordService.Rest.GetGuildChannelsAsync(_guildId)).FirstOrDefault(x => x.Id == client.ChannelId);
                if (channel is not null) await channel.ModifyAsync(x => x.Name = _databaseService.GetChannel(channel.Id).ChannelUpdatedFalseText);
            }

            channel = (await _discordService.Rest.GetGuildChannelsAsync(_guildId)).FirstOrDefault(x => x.Id == _databaseService.GetLog());
            if (channel is null) return;

            string versionActual = client.Version.Contains('|') ? client.Version[..client.Version.IndexOf('|')] : client.Version;
            string oldVersionActual = oldVersion.Contains('|') ? oldVersion[..oldVersion.IndexOf('|')] : oldVersion;

            await _discordService.Rest.SendMessageAsync(channel.Id, new MessageProperties
            {
                Content = "",
                Embeds = [
                    new EmbedProperties
                    {
                        Title = $"{client.Client} Update Detected",
                        Description = $"Version: ``{versionActual}``\nOld Version: ``{oldVersionActual}``",
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
}
