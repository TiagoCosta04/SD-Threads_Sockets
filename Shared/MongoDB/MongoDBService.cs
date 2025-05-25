using MongoDB.Driver;
using MongoDB.Bson;
using Shared.Models;
using System.Text.Json;

namespace Shared.MongoDB
{
    public class MongoDBService
    {
        private readonly IMongoDatabase _database;
        private readonly IMongoCollection<BsonDocument> _wavyMessagesCollection;
        private readonly IMongoCollection<BsonDocument> _aggregatedDataCollection;
        private readonly IMongoCollection<BsonDocument> _systemLogsCollection;

        public MongoDBService()
        {
            try
            {
                var client = new MongoClient(MongoDBConfig.CONNECTION_STRING);
                _database = client.GetDatabase(MongoDBConfig.DATABASE_NAME);
                
                _wavyMessagesCollection = _database.GetCollection<BsonDocument>(MongoDBConfig.WAVY_MESSAGES_COLLECTION);
                _aggregatedDataCollection = _database.GetCollection<BsonDocument>(MongoDBConfig.AGGREGATED_DATA_COLLECTION);
                _systemLogsCollection = _database.GetCollection<BsonDocument>(MongoDBConfig.SYSTEM_LOGS_COLLECTION);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MongoDB] Error initializing MongoDB service: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                await _database.RunCommandAsync((Command<BsonDocument>)"{ping:1}");
                Console.WriteLine("[MongoDB] Connection test successful");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MongoDB] Connection test failed: {ex.Message}");
                return false;
            }
        }

        public async Task InsertWavyMessageAsync(WavyMessage message)
        {
            try
            {
                var document = new BsonDocument
                {
                    ["wavy_id"] = message.WavyId,
                    ["agregador_id"] = message.AgregadorId,
                    ["timestamp"] = message.Timestamp,
                    ["received_at"] = DateTime.UtcNow,
                    ["sensors"] = new BsonArray(message.Sensors.Select(s => new BsonDocument
                    {
                        ["type"] = s.Type,
                        ["value"] = s.Value
                    }))
                };

                await _wavyMessagesCollection.InsertOneAsync(document);
                Console.WriteLine($"[MongoDB] Wavy message from {message.WavyId} inserted successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MongoDB] Error inserting Wavy message: {ex.Message}");
                // Don't throw to avoid breaking the main flow
            }
        }

        public async Task InsertAggregatedDataAsync(AggregatedData data)
        {
            try
            {
                var document = new BsonDocument
                {
                    ["agregador_id"] = data.AgregadorId,
                    ["timestamp"] = data.Timestamp,
                    ["received_at"] = DateTime.UtcNow,
                    ["message_count"] = data.Messages.Count,
                    ["messages"] = new BsonArray(data.Messages.Select(m => new BsonDocument
                    {
                        ["wavy_id"] = m.WavyId,
                        ["timestamp"] = m.Timestamp,
                        ["sensors"] = new BsonArray(m.Sensors.Select(s => new BsonDocument
                        {
                            ["type"] = s.Type,
                            ["value"] = s.Value
                        }))
                    }))
                };

                await _aggregatedDataCollection.InsertOneAsync(document);
                Console.WriteLine($"[MongoDB] Aggregated data from {data.AgregadorId} with {data.Messages.Count} messages inserted successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MongoDB] Error inserting aggregated data: {ex.Message}");
                // Don't throw to avoid breaking the main flow
            }
        }

        public async Task LogSystemEventAsync(string component, string eventType, string description, string? additionalData = null)
        {
            try
            {                var document = new BsonDocument
                {
                    ["component"] = component,
                    ["event_type"] = eventType,
                    ["description"] = description,
                    ["timestamp"] = DateTime.UtcNow,
                    ["additional_data"] = additionalData != null ? (BsonValue)additionalData : BsonNull.Value
                };

                await _systemLogsCollection.InsertOneAsync(document);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MongoDB] Error logging system event: {ex.Message}");
            }
        }

        public async Task<List<BsonDocument>> GetRecentWavyMessagesAsync(int limit = 100)
        {
            try
            {
                var filter = Builders<BsonDocument>.Filter.Empty;
                var sort = Builders<BsonDocument>.Sort.Descending("received_at");
                
                var cursor = await _wavyMessagesCollection.FindAsync(filter, new FindOptions<BsonDocument>
                {
                    Sort = sort,
                    Limit = limit
                });
                
                return await cursor.ToListAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MongoDB] Error retrieving recent messages: {ex.Message}");
                return new List<BsonDocument>();
            }
        }

        public async Task<Dictionary<string, object>> GetStatisticsAsync()
        {
            try
            {
                var stats = new Dictionary<string, object>();
                
                // Count total messages
                stats["total_wavy_messages"] = await _wavyMessagesCollection.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
                stats["total_aggregated_batches"] = await _aggregatedDataCollection.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
                
                // Get unique wavy nodes
                var wavyIds = await _wavyMessagesCollection.DistinctAsync<string>("wavy_id", Builders<BsonDocument>.Filter.Empty);
                stats["unique_wavy_nodes"] = (await wavyIds.ToListAsync()).Count;
                
                // Get unique aggregators
                var agrIds = await _wavyMessagesCollection.DistinctAsync<string>("agregador_id", Builders<BsonDocument>.Filter.Empty);
                stats["unique_aggregators"] = (await agrIds.ToListAsync()).Count;
                
                return stats;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MongoDB] Error getting statistics: {ex.Message}");
                return new Dictionary<string, object>();
            }
        }
    }
}
