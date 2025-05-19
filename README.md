# Exploit Update Bot

A Discord bot that monitors Roblox client versions across multiple platforms and updates designated channels to reflect whether an exploit is updated or not. It supports verified users and roles to manage update statuses and channels.

---

## Features

* Periodically checks Roblox client versions for Windows, Mac, iOS (including VNG variant), and Android (including VNG variant).
* Updates Discord channel names based on exploit update status.
* Supports slash commands for managing verified users, roles, channels, and logs.
* Permissions enforced based on verified users, roles, or owner ID.
* Environment variable configuration for flexible deployment.

---

## Environment Variables

This bot reads configuration from **environment variables** which will **override any `.env` file settings** if present.
> For clarification if set via the shell, or registry environment it'll use that instead of .env file.

| Variable       | Description                                     | Required | Example                |
| -------------- | ----------------------------------------------- | -------- | ---------------------- |
| `BOT_TOKEN`    | Discord bot token for authentication            | Yes      | `ODg2NzI1...`          |
| `OWNER_ID`     | Discord User ID of the bot owner/admin          | Yes      | `123456789012345678`   |
| `RECHECK_MS`   | Interval in milliseconds for update checks      | Yes      | `1800000` (30 minutes) |
| `EXPLOIT_NAME` | Name of the exploit for footer and logs         | Yes      | `MyExploit`            |
| `EXPLOIT_URL`  | URL shown in bot's Discord presence (streaming) | Yes      | `https://example.com`  |
| `GUILD_ID`	 | Discord GuildId to send the messagse to		   | Yes      | `123456789012345678`  |

---

## Setup & Running

1. **Set environment variables** either in your shell, CI/CD pipeline, or Docker environment.

2. **Build and run** the application.

Via Dotnet
```bash
dotnet restore
dotnet build
dotnet run
```

Via Docker
```
docker pull ghcr.io/riisdev/robloxupdatebot
```
> Volume bind /app/data

3. The bot will connect to Discord, initialize its database service, and begin monitoring Roblox client versions automatically.

4. When an update is detected, the verified users (role or user) can then run /updated <client> to adjust the text channel

5. Declare Log channel where users can see when an exploit is detected

---

## Commands

The bot supports slash commands for managing verified users, roles, channels, and logs. Permissions are restricted to verified users, verified roles, or the owner.

| Command           | Description                          | Parameters                                                  | Usage Example                                                      |
| ----------------- | ------------------------------------ | ----------------------------------------------------------- | ------------------------------------------------------------------ |
| `/update-channel` | Bind a client to a Discord channel   | `client` (enum), `channel`, `updatedName`, `notUpdatedName` | `/update-channel Android #android-updates "Updated" "Not Updated"` |
| `/add-user`       | Add a verified user                  | `user` (Discord user mention)                               | `/add-user @SomeUser`                                              |
| `/remove-user`    | Remove a verified user               | `user` (Discord user mention)                               | `/remove-user @SomeUser`                                           |
| `/add-role`       | Add a verified role                  | `roleId` (Discord Role ID)                                  | `/add-role 123456789012345678`                                     |
| `/remove-role`    | Remove a verified role               | `roleId` (Discord Role ID)                                  | `/remove-role 123456789012345678`                                  |
| `/set-log`        | Set the channel for update logs      | `channel`                                                   | `/set-log #update-logs`                                            |
| `/updated`        | Mark a client exploit as updated     | `client` (enum)                                             | `/updated Windows`                                                 |
| `/un-update`      | Mark a client exploit as not updated | `client` (enum)                                             | `/un-update Mac`                                                   |

---

## How the Database Works

The bot uses a singleton `DatabaseService` to manage data persistently (in-memory or through a custom implementation).

### Main entities managed:

* **VerifiedUsers:** Users allowed to run update commands.
* **VerifiedRoles:** Roles allowed to run update commands.
* **Channels:** Stores mappings between clients and Discord channels along with custom "updated" and "not updated" channel names.
* **Status:** Tracks the current version, bound channel, and update state (`Updated` or not) per client.
* **Log Channel:** Channel where update notifications are posted.

### Database methods used in commands:

* `GetVerifiedRoles()` — returns list of roles allowed to manage bot.
* `IsVerifiedUser(userId)` — checks if user is verified.
* `AddVerifiedUser(user)` / `RemoveVerifiedUser(user)` — manage verified users.
* `AddVerifiedRole(role)` / `RemoveVerifiedRole(role)` — manage verified roles.
* `UpdateChannel(Channel)` — updates binding between a client and a Discord channel.
* `GetStatus(clientName)` — fetch current status by client.
* `UpdateStatus(Status)` — updates status (version, updated flag, channel).
* `SetLog(channelId)` — sets log channel.
* `GetLog()` — retrieves log channel.

The database abstraction allows flexible storage backend (could be extended to a real database or persisted file).

---

## How Version Checking Works

The bot runs a periodic `UpdateService` with timers configured by `RECHECK_MS`. It checks multiple sources:

* **Windows and Mac:** Queries Roblox official client settings API for version.
* **iOS (standard and VNG):** Queries Apple iTunes Search API.
* **Android (standard and VNG):** Scrapes Google Play Store page for version.

If a new version is detected (different from stored version), the bot updates the status in the database and renames the bound Discord channel to reflect the "not updated" state and posts an embed log in the designated log channel.

---

## License

MIT License

---

Credits GPT For ReadMe Writeup
