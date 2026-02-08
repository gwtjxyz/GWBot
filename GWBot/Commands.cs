using CoenM.ImageHash;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Services.Commands;

namespace GWBot;

public class PingCommand : CommandModule<CommandContext>
{
    [Command("ping")]
    public string Ping() => $"Pong! {Math.Round(Context.Client.Latency.TotalMilliseconds)} ms";
}

public class HelpCommand : CommandModule<CommandContext>
{
    [Command("help")]
    public string Help()
    {
        return "Available commands:\n" +
        "help - Show this help message.\n" +
        "ping - Check the bot's latency.\n" +
        "refresh - Recalculate the internal image list.\n" +
        "addimage - Add a new image to the banned image list.";
    }
}

public class RefreshImageListCommand(ILogger<RefreshImageListCommand> logger) : CommandModule<CommandContext>
{
    [Command("refresh")]
    public string RefreshList()
    {
        if (Context.Message.Author is not GuildUser guildUser)
        {
            return "Invalid input: Command can only be used in a server.";
        }
        else if (!guildUser.GetPermissions(Context.Message.Guild!).HasFlag(Permissions.BanUsers))
        {
            return "You do not have permission to use this command.";
        }

        FileSystem.PopulateImageList(reset: true);

        return "Image list refreshed.";
    }
}

public class AddImageCommand(ILogger<AddImageCommand> logger, IDiscordService discordService) : CommandModule<CommandContext>
{
    [Command("addimage", "add")]
    public async Task<string> AddImage()
    {

        if (Context.Message.Author is not GuildUser guildUser)
        {
            return "Invalid input: Command can only be used in a server.";
        }
        else if (!guildUser.GetPermissions(Context.Message.Guild!).HasFlag(Permissions.BanUsers))
        {
            return "You do not have permission to use this command.";
        }

        var attachedImages = from attachment in Context.Message.Attachments
                             where attachment.ContentType is not null && attachment.ContentType.StartsWith("image/")
                             select attachment;

        if (!attachedImages.Any())
        {
            return "Invalid input: No attached images to add.";
        }

        // TODO allow links?

        var imageList = FileSystem.LoadImageList(FileAccess.Read);
        var imageAttachmentDataList = await discordService.GetImageAttachmentData(attachedImages);

        int addedImageCount = 0;

        foreach (var attachment in imageAttachmentDataList)
        {
            var matchedImages = from image in imageList.Images
                                where IsHashSimilar(image.PHash, attachment.PHash) && IsHashSimilar(image.DHash, attachment.DHash)
                                select image;

            if (matchedImages.Any())
            {
                continue;
            }

            var imageToAdd = attachedImages.Where(i => i.Id == attachment.Id).First();
            // not super optimal to retrieve image content twice, but I don't feel like
            // refactoring the code right now to avoid it, and this isn't a common code path anyway
            var imageContent = await discordService.GetImageContent(imageToAdd);
            var addedImageName = FileSystem.AddImageToFolder(imageToAdd, imageContent);
            logger.LogInformation("Added image {} (ID {}) as {}", imageToAdd.FileName, imageToAdd.Id, addedImageName);
            addedImageCount++;
        }

        // recalculate image list JSON if needed
        if (addedImageCount > 0)
            FileSystem.PopulateImageList();

        // TODO
        return $"Added {addedImageCount} images (duplicates skipped)";
    }

    private static bool IsHashSimilar(ulong hash1, ulong hash2)
    {
        return CompareHash.Similarity(hash1, hash2) >= 99.5;
    }
}
