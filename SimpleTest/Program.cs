using MongoDB.Driver;
using MongoDB.Bson;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Teste Simples de Conexão MongoDB ===\n");

        try
        {
            const string connectionString = "mongodb+srv://marracho:220902Francisco@sistemasdistribuidos.mkvei02.mongodb.net/";
            const string databaseName = "SistemasDistribuidos";

            var client = new MongoClient(connectionString);
            var database = client.GetDatabase(databaseName);
            
            // Test connection
            Console.WriteLine("Testando conexão...");
            await database.RunCommandAsync((Command<BsonDocument>)"{ping:1}");
            Console.WriteLine("✅ Conexão com MongoDB bem-sucedida!");

            // Create a simple test collection
            var testCollection = database.GetCollection<BsonDocument>("TestConnection");
            
            // Insert a test document
            var testDoc = new BsonDocument
            {
                ["message"] = "Teste de conexão",
                ["timestamp"] = DateTime.UtcNow,
                ["test_id"] = "CONNECTION_TEST_001"
            };

            await testCollection.InsertOneAsync(testDoc);
            Console.WriteLine("✅ Documento de teste inserido com sucesso!");

            // Count documents in test collection
            var count = await testCollection.CountDocumentsAsync(new BsonDocument());
            Console.WriteLine($"📊 Total de documentos na coleção de teste: {count}");

            Console.WriteLine("\n✅ Teste concluído com sucesso!");
            Console.WriteLine("Pode verificar os dados no MongoDB Compass.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Erro: {ex.Message}");
        }

        Console.WriteLine("\nPressione qualquer tecla para sair...");
        Console.ReadKey();
    }
}
