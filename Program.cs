using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using RobloxUpdateBot.Services;

_ = new EnvService(); // Load environment variables from .env file if it exists

string[] env = ["BOT_TOKEN", "OWNER_ID", "RECHECK_MS", "EXPLOIT_NAME", "EXPLOIT_URL"];

foreach (string variable in env)
{
    string? value = Environment.GetEnvironmentVariable(variable);
    if (string.IsNullOrEmpty(value))
    {
        Console.WriteLine($"Missing environment variable: {variable}");
        return;
    }
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder();
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddDiscordGateway(clientOptions =>
{
    clientOptions.Token = Environment.GetEnvironmentVariable("BOT_TOKEN");
    clientOptions.Intents = GatewayIntents.GuildMessages | GatewayIntents.Guilds | GatewayIntents.MessageContent;
    clientOptions.Presence = new PresenceProperties(UserStatusType.DoNotDisturb)
    {
        Activities =
        [
            new UserActivityProperties("Checking for updates...", UserActivityType.Streaming).WithState(Environment.GetEnvironmentVariable("EXPLOIT_URL"))
        ]
    };
})
.AddApplicationCommands();

IHost host = builder.Build();

host.AddModules(typeof(Program).Assembly);

host.UseGatewayEventHandlers();

_ = Task.Run(() => new UpdateService(host));

await host.RunAsync();