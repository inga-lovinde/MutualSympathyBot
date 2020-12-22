#load "Tools.csx"
#load "UserSympathyEntity.csx"

using Microsoft.Bot.Connector;
using Microsoft.WindowsAzure.Storage; 
using Microsoft.WindowsAzure.Storage.Table; 

public class UserSympathiesRepository
{
    private CloudTable UserSympathiesTable { get; }

    public UserSympathiesRepository(CloudTable userSympathiesTable)
    {
        this.UserSympathiesTable = userSympathiesTable;
    }

    public async Task AddSympathy(UserSympathyEntity entity)
    {
        var insertOperation = TableOperation.Insert(entity);
        await this.UserSympathiesTable.ExecuteAsync(insertOperation);
    }

    public UserSympathyEntity[] GetAllSympathies(UserInfo user)
    {
        var key = user.Key;
        var condition = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, key);
        var query = new TableQuery<UserSympathyEntity>().Where(condition);
        var result = this.UserSympathiesTable.ExecuteQuery(query);
        return result.ToArray();
    }

    public async Task<UserSympathyEntity> GetSympathyIfExists(string sourceKey, string targetKey)
    {
        var retrieveOperation = TableOperation.Retrieve<UserSympathyEntity>(sourceKey, targetKey);
        var retrievedResult = await this.UserSympathiesTable.ExecuteAsync(retrieveOperation);
        return (UserSympathyEntity)(retrievedResult?.Result);
    }

    public async Task<UserSympathyEntity> GetSympathyIfExists(UserInfo source, string targetKey)
    {
        return await GetSympathyIfExists(source.Key, targetKey);
    }

    public async Task<UserSympathyEntity> GetSympathyIfExists(UserInfo source, UserInfo target)
    {
        return await GetSympathyIfExists(source, target.Key);
    }

    public async Task DeleteSympathy(UserSympathyEntity entity)
    {
        var deleteOperation = TableOperation.Delete(entity);
        await this.UserSympathiesTable.ExecuteAsync(deleteOperation);
    }
}
