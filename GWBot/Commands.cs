using CoenM.ImageHash;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Services.Commands;

namespace GWBot;

static class CommandHelpers
{
    public static string CheckForModeratorPermissions(CommandContext context)
    {
        if (context.Message.Author is not GuildUser guildUser)
        {
            return "Invalid input: Command can only be used in a server.";
        }
        else if (!guildUser.GetPermissions(context.Message.Guild!).HasFlag(Permissions.BanUsers))
        {
            return "You do not have permission to use this command.";
        }
        return string.Empty;
    }
}

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
        "addimage, add - Add a new image to the banned image list.\n" +
        "testimage - Test an image against the existing image list and print the most similar stored image.\n" +
        "setthreshold, threshold - Test the similarity threshold images have to meet for the bot to act on them (default value is 95).\n" +
        "loghere - Set the current channel as the log channel for the bot's actions.\n" +
        "loganywhere = Unset the log channel.";
    }
}

public class RefreshImageListCommand(ILogger<RefreshImageListCommand> logger) : CommandModule<CommandContext>
{
    [Command("refresh")]
    public string RefreshList()
    {
        string permissionError = CommandHelpers.CheckForModeratorPermissions(Context);
        if (!String.IsNullOrEmpty(permissionError))
        {
            return permissionError;
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
        string permissionError = CommandHelpers.CheckForModeratorPermissions(Context);
        if (!String.IsNullOrEmpty(permissionError))
        {
            return permissionError;
        }

        var attachedImages = from attachment in Context.Message.Attachments
                             where attachment.ContentType is not null && attachment.ContentType.StartsWith("image/")
                             select attachment;

        if (!attachedImages.Any())
        {
            return "Invalid input: No attached images to add.";
        }

        // TODO allow links?
        var imageList = FileSystem.SerializeFromFile<ImageList>(FileSystem.ImageListPath, FileAccess.Read);
        var imageAttachmentDataList = await discordService.GetImageAttachmentData(attachedImages);
        var serverDictionary = FileSystem.SerializeFromFile<List<ServerDictionaryEntry>>(FileSystem.ServerDictionaryPath, FileAccess.Read);
        var entry = serverDictionary.Find(x => x.ServerId == Context.Message.GuildId);
        var threshold = entry is not null ? entry.SimilarityThreshold : 95.0;

        int addedImageCount = 0;

        foreach (var attachment in imageAttachmentDataList)
        {
            var matchedImages = from image in imageList.Images
                                where IsHashSimilar(image.PHash, attachment.PHash, threshold) && IsHashSimilar(image.DHash, attachment.DHash, threshold)
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

    private static bool IsHashSimilar(ulong hash1, ulong hash2, double threshold)
    {
        return CompareHash.Similarity(hash1, hash2) >= threshold;
    }
}

public class TestImageCommand(ILogger<SetLogChannelCommand> logger, IDiscordService discordService) : CommandModule<CommandContext>
{
    [Command("testimage")]
    public async Task<string> TestImage()
    {
        string permissionError = CommandHelpers.CheckForModeratorPermissions(Context);
        if (!String.IsNullOrEmpty(permissionError))
        {
            return permissionError;
        }


        var attachedImages = from attachment in Context.Message.Attachments
                             where attachment.ContentType is not null && attachment.ContentType.StartsWith("image/")
                             select attachment;

        if (!attachedImages.Any())
        {
            return "Invalid input: No attached images to test.";
        }

        var imageList = FileSystem.SerializeFromFile<ImageList>(FileSystem.ImageListPath, FileAccess.Read);
        var imageAttachmentDataList = await discordService.GetImageAttachmentData(attachedImages);

        var outputString = "Most similar images:\n";

        var loopIndex = 0;
        foreach (var attachment in imageAttachmentDataList)
        {
            var mostSimilarImage = imageList.Images.First();

            foreach (var storedImage in imageList.Images)
            {
                // Will only use pHash for this
                if (CompareHash.Similarity(attachment.PHash, storedImage.PHash) > CompareHash.Similarity(attachment.PHash, mostSimilarImage.PHash))
                {
                    mostSimilarImage = storedImage;
                }
            }

            outputString += $"Attachment {loopIndex}:\n\t" +
                $"pHash: **{attachment.PHash}**\n\t" +
                $"most similar to **{mostSimilarImage.Name}** with pHash **{mostSimilarImage.PHash}**\n\t" +
                $"similarity percentage: **{CompareHash.Similarity(attachment.PHash, mostSimilarImage.PHash)}**%\n";

            loopIndex++;
        }

        return outputString;
    }
}

public class SetThresholdCommand(ILogger<SetThresholdCommand> logger) : CommandModule<CommandContext>
{
    [Command("setthreshold", "threshold")]
    public string SetThreshold(string thresholdString)
    {
        var permissionError = CommandHelpers.CheckForModeratorPermissions(Context);
        if (!String.IsNullOrEmpty(permissionError))
        {
            return permissionError;
        }

        if (!double.TryParse(thresholdString, out double newThreshold))
        {
            return "Invalid input: threshold must be a floating point number.";
        }

        if (newThreshold < 0.0 || newThreshold > 100.0)
        {
            return "Invalid input: threshold must be in range between 0 and 100.";
        }

        var serverDictionary = FileSystem.SerializeFromFile<List<ServerDictionaryEntry>>(FileSystem.ServerDictionaryPath, FileAccess.Read);
        var entryIndex = serverDictionary.FindIndex(x => x.ServerId == Context.Guild!.Id);
        if (entryIndex < 0)
        {
            serverDictionary.Add(new ServerDictionaryEntry(Context.Message.Guild!.Id, Context.Message.ChannelId, newThreshold));
        }
        else
        {
            serverDictionary[entryIndex].SimilarityThreshold = newThreshold;
        }
        FileSystem.SerializeToFile(FileSystem.ServerDictionaryPath, serverDictionary);

        return $"Set similarity threshold to {newThreshold}.";
    }
}

public class SetLogChannelCommand(ILogger<SetLogChannelCommand> logger) : CommandModule<CommandContext>
{
    [Command("loghere")]
    public string SetLogChannel()
    {
        var permissionError = CommandHelpers.CheckForModeratorPermissions(Context);
        if (!String.IsNullOrEmpty(permissionError))
        {
            return permissionError;
        }

        // Cannot be null based on checks above
        var serverId = Context.Message.Guild!.Id;
        var channelId = Context.Message.Channel!.Id;
        var path = FileSystem.ServerDictionaryPath;

        var serverChannelDictionary = FileSystem.SerializeFromFile<List<ServerDictionaryEntry>>(path, FileAccess.ReadWrite);
        var index = serverChannelDictionary.FindIndex(x => x.ServerId == serverId);

        if (index < 0)
        {
            serverChannelDictionary.Add(new ServerDictionaryEntry(serverId, channelId));
        }
        else
        {
            serverChannelDictionary.GetRange(index, 1).ForEach(x => x.ChannelId = channelId); // TODO fix
        }

        FileSystem.SerializeToFile(path, serverChannelDictionary);

        return $"Log channel for server with ID {serverId} set to channel with ID {channelId}";
    }

    [Command("loganywhere")]
    public string ResetLogChannel()
    {
        var permissionError = CommandHelpers.CheckForModeratorPermissions(Context);
        if (!String.IsNullOrEmpty(permissionError))
        {
            return permissionError;
        }

        // Cannot be null based on checks above
        var serverId = Context.Message.Guild!.Id;
        var path = FileSystem.ServerDictionaryPath;

        var serverChannelDictionary = FileSystem.SerializeFromFile<List<ServerDictionaryEntry>>(path, FileAccess.ReadWrite);
        var index = serverChannelDictionary.FindIndex(x => x.ServerId == serverId);

        if (index >= 0)
        {
            serverChannelDictionary.RemoveAll(x => x.ServerId == serverId);
            FileSystem.SerializeToFile(path, serverChannelDictionary);
            return $"Unset log channel for server with ID {serverId}, actions will be logged to whichever channel the action occurred in.";
        }
        else
        {
            return $"Log channel for server with ID {serverId} not set.";
        }
    }
}
