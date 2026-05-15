using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using System.Reflection.Metadata.Ecma335;

namespace GWBot;

public struct ImageAttachmentData
{
    public ImageAttachmentData(ulong id, ulong pHash, ulong dHash) => (Id, PHash, DHash) = (id, pHash, dHash);
    public ulong Id { get; set; }
    public ulong PHash { get; set; }
    public ulong DHash { get; set; }
}

public interface IDiscordService
{
    public bool IsAuthorPrivilegedOrBot(Message message);

    public ValueTask SoftbanUser(Message message, ulong logChannelId);

    public Task<List<ImageAttachmentData>> GetImageAttachmentData(IEnumerable<Attachment> attachments);

    public Task<HttpContent> GetImageContent(Attachment imageAttachment);

    public ValueTask LogToChannel(string message, ulong channelId);
}

public class DiscordService(ILogger<DiscordService> logger, RestClient discordClient) : IDiscordService
{
    public bool IsAuthorPrivilegedOrBot(Message message)
    {
        var guildUser = message.Author as GuildUser;
        if (guildUser == null)
        {
            return true;
        }
        var guild = message.Guild;
        if (guild == null)
        {
            return true;
        }

        return guildUser.GetPermissions(guild).HasFlag(Permissions.BanUsers);
    }

    public async ValueTask SoftbanUser(Message message, ulong logChannelId)
    {
        logger.LogInformation("Attempting to ban user {} ({})", message.Author.Id, message.Author.GlobalName);

        if (IsAuthorPrivilegedOrBot(message))
        {
            FileSystem.LogToFile($"User {message.Author.Id} ({message.Author.GlobalName}) is privileged, skipping the ban.");
            return;
        }

        var guildUser = message.Author as GuildUser ?? throw new UserNotInGuildException($"User {message.Author.Id} ({message.Author.GlobalName}) is not a guild user");
        var guild = message.Guild ?? throw new MessageNotInGuildException($"Message {message.Id} is not in a guild");

        // first check if user is already banned, so we don't accidentally unban him
        try
        {
            var banInfo = await discordClient.GetGuildBanAsync(guildUser.GuildId, guildUser.Id);
            FileSystem.LogToFile($"User {guildUser.Id} ({guildUser.GlobalName} is already banned, not banning again.");
            logger.LogInformation("User {} is already banned, not banning again.", guildUser.Id);
        }
        catch (RestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogInformation("User {} is not currently banned, proceeding with ban.", guildUser.Id);

            // finally ban and unban the guy
            // ban and unban right after to clear messages
            // doing this in a catch block looks kinda stupid but eh whatever
            var deleteMessageSeconds = 60 * 60 * 24; // 1 day
            var properties = new RestRequestProperties().WithAuditLogReason($"Softban by GWBot: malicious images detected");
            await discordClient.BanGuildUserAsync(guild.Id, guildUser.Id, deleteMessageSeconds: deleteMessageSeconds, properties: properties);
            FileSystem.LogToFile($"Banned user {guildUser.Id} ({guildUser.GlobalName})");
            await discordClient.UnbanGuildUserAsync(guild.Id, guildUser.Id);
                
            FileSystem.LogToFile($"Unbanned user {guildUser.Id} ({guildUser.GlobalName})");
            await LogToChannel($"Successfully banned and unbanned user {guildUser.Id} ({guildUser.GlobalName})", logChannelId);
            logger.LogInformation("Successfully banned and unbanned user {} ({}).", guildUser.Id, guildUser.GlobalName);
        }
    }

    public async Task<List<ImageAttachmentData>> GetImageAttachmentData(IEnumerable<Attachment> imageAttachments)
    {
        var client = Client.HttpClient;
        var result = new List<ImageAttachmentData>();

        foreach (var imageAttachment in imageAttachments)
        {
            var responseTask = client.GetAsync(imageAttachment.Url);
            var response = await responseTask;

            using var imageStream = response.Content.ReadAsStream();
            var (pHash, dHash) = FileSystem.ComputeHashes(imageStream);
            result.Add(new ImageAttachmentData(imageAttachment.Id, pHash, dHash));
        }

        return result;
    }

    public async Task<HttpContent> GetImageContent(Attachment imageAttachment)
    {
        var client = Client.HttpClient;

        var responseTask = client.GetAsync(imageAttachment.Url);
        var response = await responseTask;

        return response.Content;
    }

    // If log channel is set, log to it, otherwise log to the same channel as the original message
    public async ValueTask LogToChannel(string message, ulong channelId)
    {
        var response = new MessageProperties().WithContent(message);

        await discordClient.SendMessageAsync(channelId, response);
    }
}
