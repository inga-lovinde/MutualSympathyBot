#r "Newtonsoft.Json"

using System.Xml.Linq;
using Microsoft.Bot.Connector;
using Newtonsoft.Json;

public class UserInfo
{
    public UserInfo()
    {
    }

    public static UserInfo CreateFromTelegramChannelData(string channel, dynamic infoFromChannelData)
    {
        var userInfo = new UserInfo
        {
            Channel = channel,
            Id = (infoFromChannelData.id?.ToString() ?? infoFromChannelData.user_id?.ToString()),
            Name = infoFromChannelData.username?.ToString(),
            FirstName = infoFromChannelData.first_name?.ToString(),
            LastName = infoFromChannelData.last_name?.ToString(),
        };

        if (string.IsNullOrEmpty(userInfo.Channel))
        {
            throw new System.ArgumentNullException("channel");
        }

        if (string.IsNullOrEmpty(userInfo.Id))
        {
            throw new System.ArgumentNullException("id");
        }

        return userInfo;
    }

    public static UserInfo CreateFromTelegramFrom(Activity activity)
    {
        return CreateFromTelegramChannelData(activity.ChannelId, ((dynamic)activity.ChannelData).message.@from);
    }

    public static UserInfo CreateFromTelegramForwardedFrom(Activity activity)
    {
        return CreateFromTelegramChannelData(activity.ChannelId, ((dynamic)activity.ChannelData).message.forward_from);
    }

    public static UserInfo CreateFromTelegramContact(Activity activity)
    {
        return CreateFromTelegramChannelData(activity.ChannelId, ((dynamic)activity.ChannelData).message.contact);
    }

    [JsonProperty(PropertyName = "channel")]
    public string Channel { get; set; }

    [JsonProperty(PropertyName = "id")]
    public string Id { get; set; }

    [JsonProperty(PropertyName = "name")]
    public string Name { get; set; }

    [JsonProperty(PropertyName = "first_name")]
    public string FirstName { get; set; }

    [JsonProperty(PropertyName = "last_name")]
    public string LastName { get; set; }

    [JsonIgnore]
    public string Key => $"{this.Channel.Length}_{this.Channel}_{this.Id}";

    [JsonIgnore]
    public ChannelAccount ChannelAccount => new ChannelAccount(id: this.Id, name: this.Name);

    public override string ToString()
    {
        if (!string.IsNullOrEmpty(this.Name))
        {
            return $"@{this.Name}";
        }

        return string.Join(" ", new[] { this.FirstName, this.LastName });
    }

    public XNode ToTelegramHtml()
    {
        if (!string.IsNullOrEmpty(this.Name))
        {
            return new XText(this.ToString());
        }

        return new XElement("a", new XAttribute("href", string.Empty/*$"tg://user?id={this.Id}"*/), this.ToString());
    }
}
