using CoenM.ImageHash;
using CoenM.ImageHash.HashAlgorithms;
using Microsoft.Extensions.Options;
using NetCord;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GWBot;

public struct ImageListEntry
{
    public ImageListEntry(string name, ulong pHash, ulong dHash) => (Name, PHash, DHash) = (name, pHash, dHash);

    public string Name { get; set; }
    public ulong PHash { get; set; }
    public ulong DHash { get; set; }
}

public struct ImageList
{
    public ImageList()
    {
        Images = [];
    }

    public List<ImageListEntry> Images { get; set; }
}

// class because it needs to be mutable for easier serialization
public class ServerChannelDictionaryEntry
{
    public ServerChannelDictionaryEntry(ulong serverId, ulong channelId) => (ServerId, ChannelId) = (serverId, channelId);
    public ulong ServerId { get; set; }
    public ulong ChannelId { get; set; }
}

public class FileSystem
{
    public static DirectoryInfo BotFolderRoot
    {
        get
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null && directory.Name != "GWBot")
            {
                directory = directory.Parent;
            }
            return directory!;
        }
    }

    public static DirectoryInfo PersistentConfigFolder
    {
        get
        {
            var userFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configFolderPath = Path.Join(userFolderPath, ".config");
            if (!Directory.Exists(configFolderPath))
            {
                Directory.CreateDirectory(configFolderPath);
            }
            var botConfigFolderPath = Path.Join(configFolderPath, "gwbot");
            if (!Directory.Exists(botConfigFolderPath))
            {
                Directory.CreateDirectory(botConfigFolderPath);
            }
            return new DirectoryInfo(botConfigFolderPath);
        }
    }

    private readonly static JsonSerializerOptions options = new() { WriteIndented = true };

    private readonly static string ServerDictionaryName = "dictionary.json";

    private readonly static string ImageListName = "imageList.json";

    public readonly static string ImageListPath = Path.Join(BotFolderRoot.FullName, ImageListName);

    public readonly static string ServerDictionaryPath = Path.Join(PersistentConfigFolder.FullName, ServerDictionaryName);

    public static ulong SetMessageChannelForServer(ulong guildId, ulong channelId)
    {
        throw new NotImplementedException("TODO");
    }

    public static ulong GetMessageChannelForServer(ulong guildId)
    {

        throw new NotImplementedException("TODO");
    }

    public static void PopulateImageList(bool reset = false)
    {
        var imageList = SerializeFromFile<ImageList>(ImageListPath, FileAccess.ReadWrite);

        DirectoryInfo imagesFolder;
        try
        {
            Console.WriteLine($"Trying to open the Images folder in {BotFolderRoot}");
            imagesFolder = BotFolderRoot.GetDirectories("Images").First();
        }
        catch (DirectoryNotFoundException)
        {
            Console.WriteLine($"Couldn't find Images folder, creating...");
            imagesFolder = BotFolderRoot.CreateSubdirectory("Images");
        }

        var imagesInFolder = imagesFolder.EnumerateFiles();

        // reset list in case it contains "ghost" entries or if we explicitly request it
        if (imageList.Images.Count > imagesInFolder.Count() || reset == true)
        {
            imageList.Images.Clear();
        }

        bool fileUpdated = false;

        foreach (var image in imagesInFolder)
        {
            // don't re-hash existing items
            if (imageList.Images.Exists(i => i.Name == image.Name))
            {
                continue;
            }

            fileUpdated = true;

            using var stream = File.OpenRead(image.FullName);
            var (pHash, dHash) = ComputeHashes(stream);

            imageList.Images.Add(new ImageListEntry(name: image.Name, pHash: pHash, dHash: dHash));
        }

        if (fileUpdated)
        {
            SerializeToFile(ImageListPath, imageList);
        }
    }

    public static (ulong, ulong) ComputeHashes(Stream imageStream)
    {
        // TODO replace with dependency injection
        var perceptualHash = new PerceptualHash();
        var differenceHash = new DifferenceHash();

        imageStream.Position = 0;
        var pHash = perceptualHash.Hash(imageStream);

        imageStream.Position = 0;
        var dHash = differenceHash.Hash(imageStream);

        return (pHash, dHash);
    }

    public static ImageList LoadImageList(FileAccess fileAccess)
    {
        // TODO replace JSON parsing with CSV parsing
        string imageListFilePath = Path.Join(BotFolderRoot.FullName, ImageListName);
        // TODO handle other exceptions
        using var imageListContents = File.Open(imageListFilePath, FileMode.OpenOrCreate, fileAccess);
        ImageList imageList;
        try
        {
            Console.WriteLine($"Trying to deserialize {imageListFilePath}");
            imageList = JsonSerializer.Deserialize<ImageList>(imageListContents);
        }
        catch (JsonException)
        {
            Console.WriteLine($"Serializing failed (empty file?), re-creating...");
            imageList = new ImageList();
        }
        return imageList;
    }

    // TODO do we need all these null checks?
    public static T SerializeFromFile<T>(string filePath, FileAccess fileAccess)
    {
        T data;
        try
        {
            using var fileContents = File.Open(filePath, FileMode.OpenOrCreate, fileAccess);
            Console.WriteLine($"Trying to deserialize {filePath}");
            data = JsonSerializer.Deserialize<T>(fileContents) ?? Activator.CreateInstance<T>();
        }
        catch (JsonException e)
        {
            Console.WriteLine($"Serializing failed. Reason:\n\t${e.Message}\n\tReturning empty list...");
            data = Activator.CreateInstance<T>();
        }
        catch (SystemException e)
        {
            Console.WriteLine($"Could not open {filePath} for reading\n\tReason: ${e.Message}\n\tReturning empty list...");
            data = Activator.CreateInstance<T>();
        }
        return data ?? Activator.CreateInstance<T>();
    }

    public static void SerializeToFile<T>(string filePath, T data)
    {
        Console.WriteLine($"Serializing data into {filePath}...");
        var jsonString = JsonSerializer.Serialize<T>(data, options);
        // Create instead of Open to always empty the file before writing
        using var fileContents = File.Create(filePath);

        byte[] jsonData = new UTF8Encoding(true).GetBytes(jsonString);
        fileContents.Write(jsonData, 0, jsonData.Length);
    }

    public static string AddImageToFolder(Attachment image, HttpContent imageContent)
    {
        var imagesFolder = BotFolderRoot.GetDirectories("Images").First()!;
        var imagesInFolder = imagesFolder.EnumerateFiles();

        using var inputStream = imageContent.ReadAsStream();

        // name + dash + number after dash + dot + extension
        string prefixPattern = "^.+[-][0-9]+[.][a-zA-Z]+$";
        // extension at end of name
        string extensionPattern = "[.][a-zA-Z]+$";
        string name = image.FileName;

        if (imagesInFolder.Any(i => i.Name == image.FileName))
        {
            // prefix if not unique

            var extensionMatcher = Regex.Match(name, extensionPattern);
            string extension = extensionMatcher.Value;
            string nameWithoutExtension = name.Substring(0, name.Length - extension.Length); // TODO test if it works

            if (Regex.IsMatch(name, prefixPattern))
            {
                // I'm sure there's a fancier way of doing this, but at this moment I feel too dumb to figure it out
                var numberIdx = nameWithoutExtension.LastIndexOf('-') + 1;
                var numberStr = nameWithoutExtension.Substring(numberIdx);
                var number = Int32.Parse(numberStr);
                number++;

                var strBeforeNumber = nameWithoutExtension.Substring(0, numberIdx);
                nameWithoutExtension = strBeforeNumber + number.ToString();
            }
            else
            {
                var number = 0;
                // make sure we don't accidentally overwrite anything
                while (true)
                {
                    if (imagesInFolder.Any(i => i.Name == $"{nameWithoutExtension}-{number}{extension}"))
                    {
                        number++;
                    }
                    else
                    {
                        break;
                    }
                }
                nameWithoutExtension += $"-{number}";
            }

            name = nameWithoutExtension + extension;
        }

        using var fileStream = File.Create(Path.Join(imagesFolder.FullName, name));
        inputStream.Seek(0, SeekOrigin.Begin);
        inputStream.CopyTo(fileStream);
        return name;
    }

    // TODO maybe this should be in a separate class/interface?
    public static void LogToFile(string message)
    {
        string logFilePath = Path.Join(BotFolderRoot.FullName, "banlog.txt");
        var now = DateTime.UtcNow;
        string timestamp = $"[{now:yyyy-MM-dd HH:mm:ss}] ";
        File.AppendAllText(logFilePath, timestamp + message + Environment.NewLine);
    }
}
