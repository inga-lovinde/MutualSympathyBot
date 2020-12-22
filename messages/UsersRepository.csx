#load "UserEntity.csx"

using Microsoft.WindowsAzure.Storage; 
using Microsoft.WindowsAzure.Storage.Table; 

public class UsersRepository
{
    private CloudTable UsersTable { get; }

    public UsersRepository(CloudTable usersTable)
    {
        this.UsersTable = usersTable;
    }

    public async Task AddUser(UserEntity entity)
    {
        var insertOperation = TableOperation.InsertOrReplace(entity);
        await this.UsersTable.ExecuteAsync(insertOperation);
    }

    public UserEntity[] GetAllUsers()
    {
        var result = this.UsersTable.ExecuteQuery(new TableQuery<UserEntity>());
        return result.ToArray();
    }
}
