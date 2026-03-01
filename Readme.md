# GWBot Moderation bot

Really simple bot to detect if more than 3 images get posted at once, and if so,
scan them for known scam images and "softban" the user upon detection.
Feel free to build and host this yourself if you need similar functionality
for your server.

## Running
You can run the bot by building the project and running the resulting executable:

```bash
dotnet GWBot.dll
```

There are two modes you can run the bot in: development and production. By default,
development mode is used. To run in production mode, you need to supply the `--prod`
or `--production` flag when running the executable:

```bash
dotnet GWBot.dll --prod
```

There are no functional differences between the two modes, it's just about what
appsettings file is used, so you can more conveniently test your changes during
development while also running the bot in production mode somewhere else without
having to constantly change the tokens and prefixes in the appsettings file.

## App Settings

You will need two app settings files: `appsettings.Development.json` and
`appsettings.Production.json`. The former is used when running in development mode,
and the latter is used when running in production mode. The format of both should
be as follows:

```json
{
  "Discord": {
    "Token": "your-discord-bot-token",
    "Prefix": "your-prefix-of-choice"
  }
}

```

Just having the development appsettings file is sufficient, and at some point you can create
the production file as well if you want to do what is described in the previous section.

## Usage

just run the `help` command and it will show you all the available commands and how to use them.