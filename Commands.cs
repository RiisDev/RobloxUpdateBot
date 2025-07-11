using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using RobloxUpdateBot.Services;
using System.Diagnostics.CodeAnalysis;
using Channel = RobloxUpdateBot.Services.Channel;

namespace RobloxUpdateBot
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public class Commands(DatabaseService database) : ApplicationCommandModule<ApplicationCommandContext>
    {
        private readonly ulong _ownerId = ulong.Parse(Environment.GetEnvironmentVariable("OWNER_ID")?? "0");

        public readonly Dictionary<Client, string> InternalClients = new()
        {
            { Client.Android, "Android"},
            { Client.AndroidVng, "Android-VNG"},
            { Client.Ios, "IOS"},
            { Client.IosVng, "IOS-VNG"},
            { Client.Mac, "Mac"},
            { Client.Windows, "Windows"}
        };

        public enum Client
        {
            Android,
            AndroidVng,
            Windows,
            Mac,
            IosVng,
            Ios
        }

        private bool IsUserVerified(ulong userId, IReadOnlyList<ulong> roles)
        {
            List<ulong> verifiedRoles = database.GetVerifiedRoles();
            bool roleVerified = verifiedRoles.Intersect(roles).Any();
            bool userVerified = database.IsVerifiedUser(userId);
            return userVerified || roleVerified || userId == _ownerId;
        }

        [SlashCommand("watch", "Watched a client for updates, without updating a channel.")]
        public async Task Watch(Client client)
        {
            await RespondAsync(InteractionCallback.DeferredMessage());
            if (!IsUserVerified(Context.User.Id, (Context.User as GuildUser)!.RoleIds))
            {
                await FollowupAsync(new InteractionMessageProperties
                {
                    Content = "You do not have permission for this command"
                });
                return;
            }

            Status? status = database.GetStatus(InternalClients[client]);
            if (status == null) database.UpdateStatus(new Status(InternalClients[client], "", 0, false));
            else
            {
                Status newStatus = status with { ChannelId = 0 };
                database.UpdateStatus(newStatus);
            }

            await FollowupAsync(new InteractionMessageProperties
            {
                Content = $"Successfully started watching **{client}**"
            });
        }

        [SlashCommand("unwatch", "Unwatches a client for updates, without updating a channel.")]
        public async Task UnWatch(Client client)
        {
            await RespondAsync(InteractionCallback.DeferredMessage());
            if (!IsUserVerified(Context.User.Id, (Context.User as GuildUser)!.RoleIds))
            {
                await FollowupAsync(new InteractionMessageProperties
                {
                    Content = "You do not have permission for this command"
                });
                return;
            }

            Status? status = database.GetStatus(InternalClients[client]);
            if (status == null) return;

            database.DeleteStatus(client);

            await FollowupAsync(new InteractionMessageProperties
            {
                Content = $"Successfully started watching **{client}**"
            });
        }

        [SlashCommand("update-channel", "Updates bind between client and channel")]
        public async Task UpdateChannel(Client client, IGuildChannel channel, string updatedName, string notUpdatedName)
        {
            await RespondAsync(InteractionCallback.DeferredMessage());

            if (!IsUserVerified(Context.User.Id, (Context.User as GuildUser)!.RoleIds))
            {
                await FollowupAsync(new InteractionMessageProperties
                {
                    Content = "You do not have permission for this command"
                });
                return;
            }

            database.UpdateChannel(new Channel(channel.Id, updatedName, notUpdatedName));

            Status? status = database.GetStatus(InternalClients[client]);

            if (status == null) database.UpdateStatus(new Status(InternalClients[client], "", channel.Id, false));
            else
            {
                Status newStatus = status with { ChannelId = channel.Id };
                database.UpdateStatus(newStatus);
            }

            await FollowupAsync(new InteractionMessageProperties
            {
                Content = $"Successfully bound **{client}** to #{channel.Id}, with text ``{updatedName}`` and ``{notUpdatedName}``"
            });
        }

        [SlashCommand("add-user", "Allows a user to set statuses")]
        public async Task AddVerifiedUser(GuildUser user)
        {
            await RespondAsync(InteractionCallback.DeferredMessage());

            if (!IsUserVerified(Context.User.Id, (Context.User as GuildUser)!.RoleIds))
            {
                await FollowupAsync(new InteractionMessageProperties
                {
                    Content = "You do not have permission for this command"
                });
                return;
            }

            database.AddVerifiedUser(new VerifiedUsers(user.Username, user.Id));

            await FollowupAsync(new InteractionMessageProperties
            {
                Content = $"Successfully added {user.Username}#{user.Discriminator} to the verified users list"
            });
        }

        [SlashCommand("remove-user", "Disallows a user to set statuses")]
        public async Task RemoveVerifiedUser(GuildUser user)
        {
            await RespondAsync(InteractionCallback.DeferredMessage());

            if (!IsUserVerified(Context.User.Id, (Context.User as GuildUser)!.RoleIds))
            {
                await FollowupAsync(new InteractionMessageProperties
                {
                    Content = "You do not have permission for this command"
                });
                return;
            }

            database.RemoveVerifiedUser(new VerifiedUsers(user.Username, user.Id));

            await FollowupAsync(new InteractionMessageProperties
            {
                Content = $"Successfully removed {user.Username}#{user.Discriminator} from the verified users list"
            });
        }

        [SlashCommand("add-role", "Allows a role to set statuses")]
        public async Task AddVerifiedRole(ulong roleId)
        {
            await RespondAsync(InteractionCallback.DeferredMessage());
            if (!IsUserVerified(Context.User.Id, (Context.User as GuildUser)!.RoleIds))
            {
                await FollowupAsync(new InteractionMessageProperties
                {
                    Content = "You do not have permission for this command"
                });
                return;
            }

            Role role = await Context?.Guild?.GetRoleAsync(roleId)!;
            
            database.AddVerifiedRole(new VerifiedRoles(role.Name, role.Id));

            await FollowupAsync(new InteractionMessageProperties
            {
                Content = $"Successfully added {role.Name} to the verified roles list"
            });
        }

        [SlashCommand("remove-role", "Disallows a role to set statuses")]
        public async Task RemoveVerifiedRole(ulong roleId)
        {
            await RespondAsync(InteractionCallback.DeferredMessage());
            if (!IsUserVerified(Context.User.Id, (Context.User as GuildUser)!.RoleIds))
            {
                await FollowupAsync(new InteractionMessageProperties
                {
                    Content = "You do not have permission for this command"
                });
                return;
            }
            Role role = await Context?.Guild?.GetRoleAsync(roleId)!;

            database.RemoveVerifiedRole(new VerifiedRoles(role.Name, role.Id));

            await FollowupAsync(new InteractionMessageProperties
            {
                Content = $"Successfully removed {role.Name} from the verified roles list"
            });
        }

        [SlashCommand("set-log", "Set log channel to display update logs")]
        public async Task SetLog(IGuildChannel channel)
        {
            await RespondAsync(InteractionCallback.DeferredMessage());
            if (!IsUserVerified(Context.User.Id, (Context.User as GuildUser)!.RoleIds))
            {
                await FollowupAsync(new InteractionMessageProperties
                {
                    Content = "You do not have permission for this command"
                });
                return;
            }
            database.SetLog(channel.Id);

            await FollowupAsync(new InteractionMessageProperties
            {
                Content = $"Successfully set {channel.Name} as the log channel"
            });
        }

        [SlashCommand("updated", "Declares an exploit as updated.")]
        public async Task UpdateChannel(Client client)
        {
            await RespondAsync(InteractionCallback.DeferredMessage());

            if (!IsUserVerified(Context.User.Id, (Context.User as GuildUser)!.RoleIds))
            {
                await FollowupAsync(new InteractionMessageProperties
                {
                    Content = "You do not have permission for this command"
                });
                return;
            }
            
            Status? status = database.GetStatus(InternalClients[client]);

            if (status == null)
            {
                await FollowupAsync(new InteractionMessageProperties
                {
                    Content = "Failed to update exploit, client isn't initialized"
                });
                return;
            }

            Status newStatus = status with { Updated = true };
            database.UpdateStatus(newStatus);

            IGuildChannel? channel = (await Context.Guild!.GetChannelsAsync()).FirstOrDefault(x => x.Id == status.ChannelId);

            if (channel is null)
            {
                await FollowupAsync(new InteractionMessageProperties
                {
                    Content = "Failed to update bound channel, not found."
                });
                return;
            }

            await channel.ModifyAsync(options => options.Name = database.GetChannel(channel.Id).ChannelUpdatedTrueText);


            await FollowupAsync(new InteractionMessageProperties
            {
                Content = $"Successfully updated **{client}**"
            });
        }

        [SlashCommand("un-update", "Declares an exploit as not updated.")]
        public async Task UnUpdateChannel(Client client)
        {
            await RespondAsync(InteractionCallback.DeferredMessage());

            if (!IsUserVerified(Context.User.Id, (Context.User as GuildUser)!.RoleIds))
            {
                await FollowupAsync(new InteractionMessageProperties
                {
                    Content = "You do not have permission for this command"
                });
                return;
            }

            Status? status = database.GetStatus(InternalClients[client]);

            if (status == null)
            {
                await FollowupAsync(new InteractionMessageProperties
                {
                    Content = "Failed to un-update exploit, client isn't initialized"
                });
                return;
            }

            Status newStatus = status with { Updated = false };
            database.UpdateStatus(newStatus);

            IGuildChannel? channel = (await Context.Guild!.GetChannelsAsync()).FirstOrDefault(x => x.Id == status.ChannelId);

            if (channel is null)
            {
                await FollowupAsync(new InteractionMessageProperties
                {
                    Content = "Failed to update bound channel, not found."
                });
                return;
            }

            await channel.ModifyAsync(options => options.Name = database.GetChannel(channel.Id).ChannelUpdatedFalseText);

            await FollowupAsync(new InteractionMessageProperties
            {
                Content = $"Successfully updated **{client}**"
            });
        }
    }
}
