#r "Newtonsoft.Json"
#r "System.Xml.Linq"
#load "UserEntity.csx"
#load "UserInfo.csx"
#load "UsersRepository.csx"
#load "UsersMutexService.csx"
#load "UserSympathyEntity.csx"
#load "UserSympathiesRepository.csx"

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Microsoft.Bot.Builder.Azure;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;
using Microsoft.WindowsAzure.Storage;

private static async Task ReplyWithChannelData(ConnectorClient client, Activity originalActivity, object channelData)
{
    var reply = originalActivity.CreateReply();
    reply.ChannelData = JsonConvert.SerializeObject(channelData);
    //reply.Text = "```" + Environment.NewLine + JsonConvert.SerializeObject(channelData) + Environment.NewLine + "```";
    await client.Conversations.ReplyToActivityAsync(reply);
}

private static async Task ReplyWithVideoHtml(ConnectorClient client, Activity originalActivity, string fileId, object[] xNodes)
{
    var document = new XDocument(new XElement("Root", xNodes));
    var flattenedNodes = document.Root.Nodes();

    await ReplyWithChannelData(client, originalActivity, new {
        @method = "sendDocument",
        parameters = new
        {
            document = fileId,
            caption = string.Join(string.Empty, flattenedNodes.Select(node => node.ToString(SaveOptions.DisableFormatting))),
            parse_mode = "HTML",
        },
    });
}

private static async Task ReplyWithBigHugHtml(ConnectorClient client, Activity originalActivity, params object[] xNodes)
{
    await ReplyWithVideoHtml(client, originalActivity, "CgADBAADFKAAApcdZAdWXtXmanu6FAI", xNodes);
}

private static async Task ReplyWithNowKissHtml(ConnectorClient client, Activity originalActivity, params object[] xNodes)
{
    await ReplyWithVideoHtml(client, originalActivity, "CgADBQADYgADPh0YVIDk_Yb299_TAg", xNodes);
}

private static async Task ReplyWithHtml(ConnectorClient client, Activity originalActivity, params object[] xNodes)
{
    var document = new XDocument(new XElement("Root", xNodes));
    var flattenedNodes = document.Root.Nodes();

    await ReplyWithChannelData(client, originalActivity, new {
        @method = "sendMessage",
        parameters = new
        {
            text = string.Join(string.Empty, flattenedNodes.Select(node => node.ToString(SaveOptions.DisableFormatting))),
            parse_mode = "HTML",
        },
    });
}

private static async Task ReplyWithMarkdown(ConnectorClient client, Activity originalActivity, string md)
{
    await ReplyWithChannelData(client, originalActivity, new {
        @method = "sendMessage",
        parameters = new
        {
            text = md,
            parse_mode = "Markdown",
        },
    });
}

private static async Task RunForSympathyMessage(
    ConnectorClient client,
    Activity activity,
    UserInfo sympathySource,
    UserInfo sympathyTarget,
    UserSympathiesRepository userSympathiesRepository,
    UserSympathiesRepository mutualSympathiesRepository,
    TraceWriter log)
{
    if (sympathySource.Key == sympathyTarget.Key)
    {
        await ReplyWithMarkdown(client, activity, "Nope");
        return;
    }

    var existingSympathy = await userSympathiesRepository.GetSympathyIfExists(sympathySource, sympathyTarget);
    var existingMutualSympathy = await mutualSympathiesRepository.GetSympathyIfExists(sympathySource, sympathyTarget);
    if (existingSympathy != null || existingMutualSympathy != null)
    {
        var reply0 = activity.CreateReply("You have already registered your sympathy for this person");
        await client.Conversations.ReplyToActivityAsync(reply0);
        return;
    }

    var existingSympathies = userSympathiesRepository.GetAllSympathies(sympathySource);
    if (existingSympathies.Length >= 10)
    {
        var reply0 = activity.CreateReply("You have registered too many sympathies; try removing some");
        await client.Conversations.ReplyToActivityAsync(reply0);
        return;
    }

    var sympathyEntity = new UserSympathyEntity(activity, sympathySource, sympathyTarget);

    var reverseSympathy = await userSympathiesRepository.GetSympathyIfExists(sympathyTarget, sympathySource);
    if (reverseSympathy != null)
    {
        var reverseActivity = reverseSympathy.OriginalActivity;
        await ReplyWithNowKissHtml(client, activity, new XText($"Your sympathy to {sympathyTarget} is mutual!"));
        await ReplyWithNowKissHtml(client, reverseActivity, new XText($"Your sympathy to {sympathySource} is mutual!"));

        await mutualSympathiesRepository.AddSympathy(sympathyEntity);
        await userSympathiesRepository.DeleteSympathy(reverseSympathy);
        await mutualSympathiesRepository.AddSympathy(reverseSympathy);

        var sourceMutualSympathies = mutualSympathiesRepository.GetAllSympathies(sympathySource);
        var targetMutualSympathies = mutualSympathiesRepository.GetAllSympathies(sympathyTarget);
        foreach (var commonMutual in sourceMutualSympathies.Select(s => s.RowKey).Intersect(targetMutualSympathies.Select(s => s.RowKey)))
        {
            var commonSympathy = await mutualSympathiesRepository.GetSympathyIfExists(commonMutual, sympathySource.Key);
            var commonUserInfo = commonSympathy.UserInfo;
            var text = $"Good news, {sympathySource}, {sympathyTarget}, {commonUserInfo}! You all like each other!";
            await ReplyWithBigHugHtml(client, activity, new XText(text));
            await ReplyWithBigHugHtml(client, reverseActivity, new XText(text));
            await ReplyWithBigHugHtml(client, commonSympathy.OriginalActivity, new XText(text));
        }

        return;
    }

    await userSympathiesRepository.AddSympathy(sympathyEntity);
    await ReplyWithHtml(client, activity, new XText($"You have forwarded message from {sympathyTarget} ({sympathyTarget.Id}). And you are {sympathySource} ({sympathySource.Id}). Sympathy logged!"));
}

private static IEnumerable<XNode> GetDescriptionForList(UserSympathyEntity sympathy)
{
    yield return sympathy.SympathyTargetInfo.ToTelegramHtml();
    yield return new XText($", added on {sympathy.Timestamp}.");
    if (sympathy.Timestamp.AddDays(7) < DateTimeOffset.UtcNow)
    {
        yield return new XText(Environment.NewLine);
        yield return new XElement("i", $"Forget:");
        yield return new XText($" /forget_{sympathy.RowKey}");
    }
}

private static async Task RunForList(
    ConnectorClient client,
    Activity activity,
    UserInfo user,
    UserSympathiesRepository userSympathiesRepository,
    UserSympathiesRepository mutualSympathiesRepository,
    TraceWriter log)
{
    var nodes = new List<object>();

    var sympathies = userSympathiesRepository.GetAllSympathies(user);
    if (sympathies.Length == 0)
    {
        nodes.Add(new XElement("b", "You have no registered non-mutual sympathies"));
    }
    else
    {
        nodes.Add(new XElement("b", "Current sympathies"));
        nodes.AddRange(sympathies.Select(sympathy => Enumerable.Repeat((XNode)(new XText(Environment.NewLine)), 2).Concat(GetDescriptionForList(sympathy))));
    }

    var mutuals = mutualSympathiesRepository.GetAllSympathies(user);
    if (mutuals.Length != 0)
    {
        nodes.Add(new XText(Environment.NewLine));
        nodes.Add(new XText(Environment.NewLine));
        nodes.Add(new XElement("b", "Mutual sympathies"));
        nodes.AddRange(mutuals.Select(sympathy => Enumerable.Repeat((XNode)(new XText(Environment.NewLine)), 1).Concat(GetDescriptionForList(sympathy))));
    }

    await ReplyWithHtml(client, activity, nodes);
}

private static async Task RunForDelete(ConnectorClient client, Activity activity, UserInfo user, string sympathyTargetKey, UserSympathiesRepository userSympathiesRepository, UserSympathiesRepository mutualSympathiesRepository, TraceWriter log)
{
    var existingSympathy = await userSympathiesRepository.GetSympathyIfExists(user, sympathyTargetKey);

    if (existingSympathy.Timestamp.AddDays(7) >= DateTimeOffset.UtcNow)
    {
        var reply0 = activity.CreateReply("You have to wait 30 days until you can forget this sympathy");
        await client.Conversations.ReplyToActivityAsync(reply0);
        return;
    }

    await userSympathiesRepository.DeleteSympathy(existingSympathy);
    await RunForList(client, activity, user, userSympathiesRepository, mutualSympathiesRepository, log);
}

private static async Task RunForAdmin(ConnectorClient client, Activity activity, Func<Task> adminAction)
{
    if (activity.From.Id.ToString() != "812607159")
    {
        var reply0 = activity.CreateReply();
        reply0.Text = $"Nice try, {activity.From.Name}";
        await client.Conversations.ReplyToActivityAsync(reply0);
        return;
    }

    await adminAction();
}

private static async Task RunForBroadcastMessage(ConnectorClient client, Activity activity, string broadcastText, UsersRepository usersRepository, TraceWriter log)
{
    await RunForAdmin(client, activity, async () => {
        var messagesSent = 0;
        List<string> usersFailed = new List<string>();
        // NOTE: Sensitive data processing here,
        // DO NOT memorize specific users, only store the messages count
        foreach (var userEntity in usersRepository.GetAllUsers())
        {
            var broadcastReply = userEntity.OriginalActivity.CreateReply($"Message from @{activity.From.Name}: {broadcastText}");
            try
            {
                await client.Conversations.ReplyToActivityAsync(broadcastReply);
                messagesSent++;
            }
            catch (Exception)
            {
                usersFailed.Add(userEntity.PartitionKey);
            }
        }

        var reply = activity.CreateReply($"Message broadcast sent to {messagesSent} users: {broadcastText}");
        await client.Conversations.ReplyToActivityAsync(reply);

        if (usersFailed.Any())
        {
            reply = activity.CreateReply($"Failed to send message to {usersFailed.Count} users: {string.Join(",", usersFailed)}");
            await client.Conversations.ReplyToActivityAsync(reply);
        }
    });
}

private static async Task RunForStats(
    ConnectorClient client,
    Activity activity,
    UsersRepository usersRepository,
    UserSympathiesRepository userSympathiesRepository,
    UserSympathiesRepository mutualSympathiesRepository,
    TraceWriter log
)
{
    await RunForAdmin(client, activity, async () => {
        var usersCount = 0;
        var sympathiesCount = 0;
        var mutualSympathiesCount = 0;
        // NOTE: Sensitive data processing here,
        // DO NOT disaggregate per-user sympathies,
        // DO NOT process separate users,
        // ONLY aggregation of total users count / sympathies count (per-bot, NOT per-user) is allowed
        foreach (var userEntity in usersRepository.GetAllUsers())
        {
            usersCount++;
            try {
                sympathiesCount += userSympathiesRepository.GetAllSympathies(userEntity.UserInfo).Count();
                mutualSympathiesCount += mutualSympathiesRepository.GetAllSympathies(userEntity.UserInfo).Count();
            } catch(Exception) {
                var debugReply = activity.CreateReply("Malformed user:" + Environment.NewLine + "```" + Environment.NewLine + userEntity.PartitionKey + Environment.NewLine + JsonConvert.SerializeObject(userEntity.UserInfo) + Environment.NewLine + "```");
                await client.Conversations.ReplyToActivityAsync(debugReply);
                throw;
            }
        }

        var reply = activity.CreateReply($"Total: {usersCount} users, {sympathiesCount} non-mutual sympathies, {mutualSympathiesCount} mutual sympathies");
        await client.Conversations.ReplyToActivityAsync(reply);
    });
}

private static async Task RunForSimpleMessage(ConnectorClient client, Activity activity, TraceWriter log)
{
    //await ReplyWithMarkdown(client, activity, $"```{Environment.NewLine}{JsonConvert.SerializeObject(activity)}{Environment.NewLine}```");
    await ReplyWithHtml(client, activity, new XText("Forward me someone else's message (preferred), or share their contact with me"));
}

private static async Task RunForPrivateAccount(ConnectorClient client, Activity activity, string name, TraceWriter log)
{
    await ReplyWithHtml(client, activity, new object[] {
        new XText("Unfortunately, "),
        new XElement("b", new XText(name)),
        new XText(" does not allow their contact to be shared, so we have no way of knowing their user_id"),
    });
}

private static async Task RunForHelp(ConnectorClient client, Activity activity, TraceWriter log)
{
    await ReplyWithHtml(
        client,
        activity,
        new object[] {
            new XText("Forward me someone else's message, and I'll remember that you like them (you can make me forget about that by using commands from /list)"),
            new XText(Environment.NewLine),
            new XText("Alternatively, you can share their contact with me, but message forwarding works better."),
            new XText(Environment.NewLine),
            new XText("Once they will forward me your message, I'll notify both of you that you like each other! Until then, I will keep silence."),
            new XText(Environment.NewLine),
            new XText("What's more, if you two have some common person with mutual sympathies between all three of you, I'll notify all three about that!"),
            new XText(Environment.NewLine),
            new XText(Environment.NewLine),
            new XText("The idea behind this bot is that: there is a lot of introverted/shy people, who don't want to inform their crush of the feelings towards them in case these feelings are not mutual. This can result in two people having feelings toward each other, with both being afraid of expressing their feeling because each of them is not sure if other will reciprocate."),
            new XText(Environment.NewLine),
            new XText("The bot attempts to solve this problem, working as an escrow for people's \"feelings\", so that: (a) if two people like each other, bot will inform both of them of their feelings, and (b) if a person X likes person Y, and the feeling is not returned, bot will keep X's secret."),
            new XText(Environment.NewLine),
            new XText("There are also basic abuse protections in place: the number of non-mutual sympathies one can express at a time is limited, and there is a reverse cooling-off period, during which a fresh sympathy cannot be cancelled. These limitations are intended to prevent malicious user to brute-force who likes them and who doesn't."),
            new XText(Environment.NewLine),
            new XText(Environment.NewLine),
            new XElement("b", new XText("Note that in order for bot to work, you both should have enabled contact sharing in telegram!")),
            new XText(" (in Telegram privacy settings -> Forwarded Messages, either set to Everybody or add @MutualSympathyBot to \"always allow\")"),
            new XText(Environment.NewLine),
            new XText(Environment.NewLine),
            new XText("If you have any feedback or suggestions, feel free to contact my creator @inga_lovinde"),
        });
}

private static async Task RunForMessage(
    Activity activity,
    UsersRepository usersRepository,
    UserSympathiesRepository userSympathiesRepository,
    UserSympathiesRepository mutualSympathiesRepository,
    UsersMutexService usersMutexService,
    TraceWriter log)
{
    var client = new ConnectorClient(new Uri(activity.ServiceUrl));

    if (activity.ChannelId != "telegram")
    {
        var reply = activity.CreateReply();
        reply.Text = $"Only telegram is supported at the moment, and you're using {activity.ChannelId}";
        await client.Conversations.ReplyToActivityAsync(reply);
        return;
    }

    var userInfo = UserInfo.CreateFromTelegramFrom(activity);
    await usersRepository.AddUser(new UserEntity(activity, userInfo));

    var mutex = await usersMutexService.Enter(activity);

    try
    {
        //var replyDebug = activity.CreateReply("Debug: " + Environment.NewLine + "```" + Environment.NewLine + JsonConvert.SerializeObject(activity) + Environment.NewLine + "```");
        //await client.Conversations.ReplyToActivityAsync(replyDebug);

        var contact = ((dynamic)activity.ChannelData)?.message?.contact;
        if (contact != null) {
            await RunForSympathyMessage(
                client,
                activity,
                userInfo,
                UserInfo.CreateFromTelegramContact(activity),
                userSympathiesRepository,
                mutualSympathiesRepository,
                log);

            return;
        }

        var forwardedFrom = ((dynamic)activity.ChannelData)?.message?.forward_from;
        if (forwardedFrom != null)
        {
            await RunForSympathyMessage(
                client,
                activity,
                userInfo,
                UserInfo.CreateFromTelegramForwardedFrom(activity),
                userSympathiesRepository,
                mutualSympathiesRepository,
                log);

            return;
        }

        var text = activity?.Text ?? string.Empty;

        if (text == "/help")
        {
            await RunForHelp(client, activity, log);
        }
        else if (text == "/list")
        {
            await RunForList(
                client,
                activity,
                userInfo,
                userSympathiesRepository,
                mutualSympathiesRepository,
                log);
        }
        else if (text.StartsWith("/forget_"))
        {
            await RunForDelete(
                client,
                activity,
                userInfo,
                text.Substring(8),
                userSympathiesRepository,
                mutualSympathiesRepository,
                log);
        }
        else if (text.StartsWith("/broadcast "))
        {
            await RunForBroadcastMessage(
                client,
                activity,
                text.Substring(11),
                usersRepository,
                log);
        }
        else if (text == "/stats")
        {
            await RunForStats(
                client,
                activity,
                usersRepository,
                userSympathiesRepository,
                mutualSympathiesRepository,
                log);
        }
        else
        {
            var forwardedFromName = ((dynamic)activity.ChannelData)?.message?.forward_sender_name;
            if (forwardedFromName != null) {
                await RunForPrivateAccount(client, activity, forwardedFromName.ToString(), log);
            } else {
                await RunForSimpleMessage(client, activity, log);
            }
        }
    }
    /*catch (Exception e) {
        var replyDebug = activity.CreateReply("Error:" + Environment.NewLine + "```" + Environment.NewLine + JsonConvert.SerializeObject(activity) + Environment.NewLine + "```" + Environment.NewLine + Environment.NewLine + "```" + e + "```");
        await client.Conversations.ReplyToActivityAsync(replyDebug);
    }*/
    finally
    {
        await mutex.Leave();
    }
}

public static async Task<object> Run(HttpRequestMessage req, TraceWriter log)
{
    // Initialize the azure bot
    using (BotService.Initialize())
    {
        // Deserialize the incoming activity
        string jsonContent = await req.Content.ReadAsStringAsync();
        var activity = JsonConvert.DeserializeObject<Activity>(jsonContent);
        if (activity.GetActivityType() != "trigger" && activity.GetActivityType() != ActivityTypes.ConversationUpdate)
        {
            log.Info($"Webhook was triggered! Content: " + jsonContent);
        }
        
        // authenticate incoming request and add activity.ServiceUrl to MicrosoftAppCredentials.TrustedHostNames
        // if request is authenticated
        if (!await BotService.Authenticator.TryAuthenticateAsync(req, new [] {activity}, CancellationToken.None))
        {
            return BotAuthenticator.GenerateUnauthorizedResponse(req);
        }
    
        if (activity != null)
        {
            var storageAccount = CloudStorageAccount.Parse(Utils.GetAppSetting("AzureWebJobsStorage"));
            var tableClient = storageAccount.CreateCloudTableClient();
            var usersRepository = new UsersRepository(tableClient.GetTableReference("sympathyBotUsers"));
            var userSympathiesRepository = new UserSympathiesRepository(tableClient.GetTableReference("sympathyBotUserSympathies"));
            var mutualSympathiesRepository = new UserSympathiesRepository(tableClient.GetTableReference("sympathyBotMutualUserSympathies"));
            var usersMutexService = new UsersMutexService(tableClient.GetTableReference("sympathyBotUserMutexes"));

            // one of these will have an interface and process it
            switch (activity.GetActivityType())
            {
                case ActivityTypes.Message:
                    await RunForMessage(
                        activity,
                        usersRepository,
                        userSympathiesRepository,
                        mutualSympathiesRepository,
                        usersMutexService,
                        log);
                    break;
                case ActivityTypes.ConversationUpdate:
                case ActivityTypes.Event:
                case ActivityTypes.ContactRelationUpdate:
                case ActivityTypes.Typing:
                case ActivityTypes.DeleteUserData:
                case ActivityTypes.Ping:
                default:
                    log.Error($"Unknown activity type ignored: {activity.GetActivityType()}"); 
                    break;
            }
        }

        return req.CreateResponse(HttpStatusCode.Accepted);
    }
}
