using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;

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
    public ValueTask SoftbanUser(Message message);

    public Task<List<ImageAttachmentData>> GetImageAttachmentData(IEnumerable<Attachment> attachments);

    public Task<HttpContent> GetImageContent(Attachment imageAttachment);

    public ValueTask LogToChannel(string message, Message originalMessage);
}

public class DiscordService(ILogger<DiscordService> logger, RestClient discordClient) : IDiscordService
{
    public async ValueTask SoftbanUser(Message message)
    {
        logger.LogInformation("Attempting to ban user {} ({})", message.Author.Id, message.Author.GlobalName);

        var guildUser = message.Author as GuildUser;
        if (guildUser is null)
        {
            logger.LogWarning("User {} is not a guild user, returning", message.Author.Id);
            return;
        }
        if (message.Guild is null)
        {
            logger.LogWarning("Message {} is not in a guild, returning", message.Id);
            return;
        }

        if (guildUser.GetPermissions(message.Guild).HasFlag(Permissions.BanUsers))
        {
            // TODO cleanup
            FileSystem.LogToFile($"User {guildUser.Id} ({guildUser.GlobalName}) is a moderator, skipping the ban.");
            await LogToChannel($"User {guildUser.Id} ({guildUser.GlobalName}) is a moderator, skipping the ban.", message);
            logger.LogInformation("User {} ({}) is a moderator, not banning.", guildUser.Id, guildUser.GlobalName);
        }
        else
        {
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
                await discordClient.BanGuildUserAsync(message.Guild.Id, message.Author.Id, deleteMessageSeconds: deleteMessageSeconds, properties: properties);
                FileSystem.LogToFile($"Banned user {message.Author.Id} ({message.Author.GlobalName})");
                await discordClient.UnbanGuildUserAsync(message.Guild.Id, message.Author.Id);
                
                FileSystem.LogToFile($"Unbanned user {message.Author.Id} ({message.Author.GlobalName})");
                await LogToChannel($"Successfully banned and unbanned user {message.Author.Id} ({message.Author.GlobalName})", message);
                logger.LogInformation("Successfully banned and unbanned user {} ({}).", message.Author.Id, message.Author.GlobalName);
            }
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
    public async ValueTask LogToChannel(string message, Message originalMessage)
    {
        var serverChannelDictionary = FileSystem.SerializeFromFile<List<ServerChannelDictionaryEntry>>(FileSystem.ServerDictionaryPath, FileAccess.Read);
        var response = new MessageProperties().WithContent(message);
        
        // Send to log channel if set, otherwise to the same channel as the original message
        var serverEntry = serverChannelDictionary.FindAll(x => x.ServerId == originalMessage.Guild!.Id);
        if (serverEntry.Count == 0)
        {
            await discordClient.SendMessageAsync(originalMessage.ChannelId, response);
        }
        else
        {
            serverEntry.ForEach(async e => await discordClient.SendMessageAsync(e.ChannelId, response));
        }
    }
}
