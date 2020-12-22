#load "UserEntity.csx"

using System;

using Microsoft.Bot.Connector;
using Microsoft.WindowsAzure.Storage; 
using Microsoft.WindowsAzure.Storage.Table; 

public class UsersMutexService
{
    public interface IMutex
    {
        Task Leave();
    }

    private class TableMutex : IMutex
    {
        private CloudTable Table { get; }

        private TableEntity Entity { get; }

        public TableMutex(CloudTable table, TableEntity entity)
        {
            this.Table = table;
            this.Entity = entity;
        }

        public async Task Leave()
        {
            var operation = TableOperation.Delete(this.Entity);
            await this.Table.ExecuteAsync(operation);
        }
    }

    private CloudTable UsersMutexTable { get; }

    public UsersMutexService(CloudTable usersMutexTable)
    {
        this.UsersMutexTable = usersMutexTable;
    }

    public async Task<IMutex> Enter(Activity activity)
    {
        var newEntity = new TableEntity(Tools.FormatUserId(activity.ChannelId, activity.From), "_");
        var retrieveOperation = TableOperation.Retrieve<TableEntity>(newEntity.PartitionKey, newEntity.RowKey);
        var oldEntityUntyped = (await this.UsersMutexTable.ExecuteAsync(retrieveOperation)).Result;
        
        if (oldEntityUntyped != null)
        {
            var oldEntity = (TableEntity)oldEntityUntyped;
            if (oldEntity.Timestamp.AddHours(1) < DateTimeOffset.UtcNow)
            {
                var deleteOperation = TableOperation.Delete(oldEntity);
                await this.UsersMutexTable.ExecuteAsync(deleteOperation);
            }
        }

        var insertOperation = TableOperation.Insert(newEntity);
        await this.UsersMutexTable.ExecuteAsync(insertOperation);
        var savedEntity = (await this.UsersMutexTable.ExecuteAsync(retrieveOperation)).Result;
        return new TableMutex(this.UsersMutexTable, (TableEntity)savedEntity);
    }
}
