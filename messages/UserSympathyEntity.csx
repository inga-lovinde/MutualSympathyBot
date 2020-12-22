#r "Newtonsoft.Json"
#load "Tools.csx"
#load "UserInfo.csx"

using Newtonsoft.Json;
using Microsoft.Bot.Connector;
using Microsoft.WindowsAzure.Storage.Table;

public class UserSympathyEntity : TableEntity
{
    public UserSympathyEntity() {}

    public UserSympathyEntity(Activity activity, UserInfo sympathySource, UserInfo sympathyTarget)
        : base(sympathySource.Key, sympathyTarget.Key)
    {
        this.RawUserInfo = JsonConvert.SerializeObject(sympathySource);
        this.RawOriginalActivity = JsonConvert.SerializeObject(activity);
        this.RawSympathyTarget = JsonConvert.SerializeObject(sympathyTarget);
    }

    public string RawUserInfo { get; set; }

    public string RawOriginalActivity { get; set; }

    public string RawSympathyTarget { get; set; }

    public UserInfo UserInfo => JsonConvert.DeserializeObject<UserInfo>(this.RawUserInfo);

    public Activity OriginalActivity => JsonConvert.DeserializeObject<Activity>(this.RawOriginalActivity);

    public UserInfo SympathyTargetInfo => JsonConvert.DeserializeObject<UserInfo>(this.RawSympathyTarget);
}