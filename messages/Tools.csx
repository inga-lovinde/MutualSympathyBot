using Microsoft.Bot.Connector;

public static class Tools
{
    public static string FormatUserId(string channelId, ChannelAccount user)
    {
        return $"{channelId.Length}_{channelId}_{user.Id}";
    }
}