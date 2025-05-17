namespace RobloxUpdateBot.Services;

public record Channel(
    ulong ChannelId, // Unique Channel Id
    string ChannelUpdatedTrueText,
    string ChannelUpdatedFalseText
);

public record Status(
    string Client, // Unique client name
    string Version,
    ulong ChannelId, // Unique Channel Id Based above
    bool Updated
);

public record History(
    string Client, // Unique client name based on Status
    string Version,
    DateTime Date
);

public record VerifiedUsers(
    string Name, // User's name
    ulong DiscordId // Unique Discord Id
);

public record VerifiedRoles(
    string Name, // Role Name
    ulong RoleId // Unique Role Id
);