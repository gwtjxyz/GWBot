using GWBot;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.Commands;
using NetCord.Services.Commands;

Console.WriteLine("Starting GWBot...");

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddDiscordGateway(options =>
    {
        options.Intents = GatewayIntents.GuildMessages | GatewayIntents.MessageContent | GatewayIntents.GuildUsers | GatewayIntents.Guilds;
    })
    .AddSingleton<IDiscordService, DiscordService>()
    .AddGatewayHandlers(typeof(Program).Assembly)
    .AddCommands(options =>
    {
        options.ResultHandler = new CustomCommandResultHandler<CommandContext>();
    });

string projectDirectory = FileSystem.BotFolderRoot.ToString();

if (args.ContainsAny("--production", "--prod"))
{
    // Production
    builder.Configuration.AddJsonFile(Path.Join(projectDirectory, "appsettings.Production.json"), optional: false, reloadOnChange: true);
}
else
{
    // Development
    builder.Configuration.AddJsonFile(Path.Join(projectDirectory, "appsettings.Development.json"), optional: false, reloadOnChange: true);
}


var host = builder.Build();

host.AddModules(typeof(Program).Assembly);

FileSystem.PopulateImageList();

await host.RunAsync();
