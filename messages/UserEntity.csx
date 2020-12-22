#r "Newtonsoft.Json"
#load "Tools.csx"

using Newtonsoft.Json;
using Microsoft.Bot.Connector;
using Microsoft.WindowsAzure.Storage.Table;

public class UserEntity : TableEntity
{
    public UserEntity() {}

    public UserEntity(Activity activity, UserInfo userInfo) : base(userInfo.Key, "_")
    {
        this.RawUserInfo = JsonConvert.SerializeObject(userInfo);
        this.RawOriginalActivity = JsonConvert.SerializeObject(activity);
    }

    public string RawUserInfo { get; set; }

    public string RawOriginalActivity { get; set; }

    public UserInfo UserInfo => JsonConvert.DeserializeObject<UserInfo>(this.RawUserInfo);

    public Activity OriginalActivity => JsonConvert.DeserializeObject<Activity>(this.RawOriginalActivity);
}