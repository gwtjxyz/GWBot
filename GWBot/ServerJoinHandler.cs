using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;

namespace GWBot;

public class ServerJoinHandler(ILogger<MessageCreateHandler> logger) : IGuildCreateGatewayHandler
{
    public async ValueTask HandleAsync(GuildCreateEventArgs arg)
    {
        // Add a new entry to the server dictionary to keep track of its settings
        var serverDictionary = FileSystem.SerializeFromFile<List<ServerDictionaryEntry>>(FileSystem.ServerDictionaryPath, FileAccess.Read);
        var entry = serverDictionary.Find(x => x.ServerId == arg.GuildId);

        if (entry == null)
        {
            serverDictionary.Add(new ServerDictionaryEntry(arg.GuildId, 0));
            FileSystem.SerializeToFile(FileSystem.ServerDictionaryPath, serverDictionary);
        }

        return;
    }
}
