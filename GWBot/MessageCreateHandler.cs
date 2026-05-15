using CoenM.ImageHash;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Rest;

namespace GWBot;

public class MessageCreateHandler(ILogger<MessageCreateHandler> logger, IDiscordService discordService) : IMessageCreateGatewayHandler
{
    public async ValueTask HandleAsync(Message message)
    {
        // Check if user is privileged, if so, ignore message
        if (discordService.IsAuthorPrivileged(message))
        {
            return;
        }

        // Check for image attachments
        var imageAttachments = from attachment in message.Attachments
                               where attachment.ContentType is not null && attachment.ContentType.StartsWith("image/")
                               select attachment;

        // Only process for spam if there are 3 or more images attached and if it was posted in a server
        if (imageAttachments.Count() < 3 || message.Guild is null)
            return;

        var client = Client.HttpClient;

        var imageList = FileSystem.SerializeFromFile<ImageList>(FileSystem.ImageListPath, FileAccess.Read);
        var imageAttachmentDataList = await discordService.GetImageAttachmentData(imageAttachments);

        bool imageFoundInList = false;
        ImageAttachmentData? mostSimilarAttachment = null;
        ImageListEntry? mostSimilarImage = null;
        double maxPHashSimilarity = 0.0, maxDHashSimilarity = 0.0;

        var serverDictionary = FileSystem.SerializeFromFile<List<ServerDictionaryEntry>>(FileSystem.ServerDictionaryPath, FileAccess.Read);
        var serverEntry = serverDictionary.Find(x => x.ServerId == message.Guild.Id);
        var logChannelId = serverEntry?.ChannelId ?? message.ChannelId;
        var threshold = serverEntry?.SimilarityThreshold ?? 95.0;

        // go thru each image in uploaded list, compare each with our image library
        // if a match is found, break and ban user, otherwise log most similar comparison result and do nothing

        foreach (var image in imageList.Images)
        {
            if (imageFoundInList)
                break;

            foreach (var attachment in imageAttachmentDataList)
            {
                double pHashSimilarity = CompareHash.Similarity(attachment.PHash, image.PHash);
                double dHashSimilarity = CompareHash.Similarity(attachment.DHash, image.DHash);

                if (pHashSimilarity > maxPHashSimilarity && dHashSimilarity > maxDHashSimilarity)
                {
                    maxPHashSimilarity = pHashSimilarity;
                    maxDHashSimilarity = dHashSimilarity;
                    mostSimilarAttachment = attachment;
                    mostSimilarImage = image;
                }

                if (pHashSimilarity >= threshold || dHashSimilarity >= threshold)
                {
                    imageFoundInList = true;
                    maxPHashSimilarity = pHashSimilarity;
                    maxDHashSimilarity = dHashSimilarity;
                    mostSimilarAttachment = attachment;
                    mostSimilarImage = image;

                    break;
                }
            }
        }

        if (imageFoundInList) // bad image detected
        {
            logger.LogInformation("Found match between image attachment {} and stored image {} (pHash: {}, dHash: {})",
                mostSimilarAttachment!.Value.Id, mostSimilarImage!.Value.Name, maxPHashSimilarity, maxDHashSimilarity);
            await discordService.SoftbanUser(message, logChannelId);
        }
        else
        {
            logger.LogInformation("No malicious images detected; most similar match was between image attachment {} and stored image {} (pHash: {}, dHash: {})",
                mostSimilarAttachment!.Value.Id, mostSimilarImage!.Value.Name, maxPHashSimilarity, maxDHashSimilarity);
        }

        return;
    }
}
