# GWBot Moderation bot

Really simple bot to detect if more than 3 images get posted at once, and if so,
scan them for known scam images and "softban" the user upon detection.
Feel free to build and host this yourself if you need similar functionality
for your server.

## App Settings

You have to include a file called `appsettings.json` into the folder with
the source files. The format of it should be as follows:

```json
{
  "Discord": {
    "Token": "your-discord-bot-token",
    "Prefix": "your-prefix-of-choice"
  }
}

```
