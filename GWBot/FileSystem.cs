using CoenM.ImageHash;
using CoenM.ImageHash.HashAlgorithms;
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

    private readonly static JsonSerializerOptions options = new() { WriteIndented = true };

    public static void PopulateImageList(bool reset = false)
    {
        var imageList = LoadImageList(FileAccess.ReadWrite);

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
            Console.WriteLine("New images detected, serializing into file list...");
            var imageListJsonString = JsonSerializer.Serialize<ImageList>(imageList, options);

            string imageListFilePath = Path.Join(BotFolderRoot.FullName, "imageList.json");
            using var imageListContents = File.Open(imageListFilePath, FileMode.OpenOrCreate, FileAccess.Write);

            byte[] data = new UTF8Encoding(true).GetBytes(imageListJsonString);
            imageListContents.Write(data, 0, data.Length);
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
        string imageListFilePath = Path.Join(BotFolderRoot.FullName, "imageList.json");
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

        using var fileStream = File.Create(Path.Join(imagesFolder.FullName, "name"));
        inputStream.Seek(0, SeekOrigin.Begin);
        inputStream.CopyTo(fileStream);
        return name;
    }
}
