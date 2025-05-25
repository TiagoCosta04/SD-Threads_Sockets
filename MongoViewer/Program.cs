using MongoDB.Driver;
using MongoDB.Bson;

class Program
{
    private static IMongoDatabase? database;
    private static readonly string connectionString = "mongodb+srv://marracho:220902Francisco@sistemasdistribuidos.mkvei02.mongodb.net/";
    private static readonly string databaseName = "SistemasDistribuidos";

    static async Task Main(string[] args)
    {
        Console.WriteLine("=== MongoDB Data Viewer - Sistemas Distribuídos ===\n");

        try
        {
            // Initialize connection
            var client = new MongoClient(connectionString);
            database = client.GetDatabase(databaseName);
            
            // Test connection
            await database.RunCommandAsync((Command<BsonDocument>)"{ping:1}");
            Console.WriteLine("✅ Conectado ao MongoDB com sucesso!\n");

            while (true)
            {
                ShowMenu();
                var choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        await ShowCollections();
                        break;
                    case "2":
                        await ShowWavyMessages();
                        break;
                    case "3":
                        await ShowAggregatedData();
                        break;
                    case "4":
                        await ShowSystemLogs();
                        break;
                    case "5":
                        await ShowStatistics();
                        break;
                    case "6":
                        await ShowRealtimeData();
                        break;
                    case "0":
                        Console.WriteLine("Encerrando...");
                        return;
                    default:
                        Console.WriteLine("Opção inválida!");
                        break;
                }

                Console.WriteLine("\nPressione qualquer tecla para continuar...");
                Console.ReadKey();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Erro: {ex.Message}");
            Console.ReadKey();
        }
    }

    static void ShowMenu()
    {
        Console.Clear();
        Console.WriteLine("=== MENU PRINCIPAL ===");
        Console.WriteLine("1. Mostrar Coleções");
        Console.WriteLine("2. Ver Mensagens Wavy");
        Console.WriteLine("3. Ver Dados Agregados");
        Console.WriteLine("4. Ver Logs do Sistema");
        Console.WriteLine("5. Estatísticas Gerais");
        Console.WriteLine("6. Monitoramento em Tempo Real");
        Console.WriteLine("0. Sair");
        Console.Write("\nEscolha uma opção: ");
    }

    static async Task ShowCollections()
    {
        Console.WriteLine("\n=== COLEÇÕES NO BANCO ===");
        try
        {
            var collections = await database.ListCollectionNamesAsync();
            var collectionList = await collections.ToListAsync();
            
            if (collectionList.Count == 0)
            {
                Console.WriteLine("Nenhuma coleção encontrada.");
                return;
            }

            foreach (var collection in collectionList)
            {
                var coll = database.GetCollection<BsonDocument>(collection);
                var count = await coll.CountDocumentsAsync(new BsonDocument());
                Console.WriteLine($"📂 {collection}: {count} documentos");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro: {ex.Message}");
        }
    }

    static async Task ShowWavyMessages()
    {
        Console.WriteLine("\n=== ÚLTIMAS MENSAGENS WAVY ===");
        try
        {
            var collection = database.GetCollection<BsonDocument>("WavyMessages");
            var filter = Builders<BsonDocument>.Filter.Empty;
            var sort = Builders<BsonDocument>.Sort.Descending("received_at");
            
            var messages = await collection.Find(filter)
                .Sort(sort)
                .Limit(10)
                .ToListAsync();

            if (messages.Count == 0)
            {
                Console.WriteLine("Nenhuma mensagem encontrada.");
                return;
            }

            foreach (var message in messages)
            {
                Console.WriteLine($"🌊 Wavy: {message.GetValue("wavy_id", "N/A")}");
                Console.WriteLine($"   Agregador: {message.GetValue("agregador_id", "N/A")}");
                Console.WriteLine($"   Timestamp: {message.GetValue("timestamp", "N/A")}");
                Console.WriteLine($"   Recebido: {message.GetValue("received_at", DateTime.MinValue)}");
                
                if (message.Contains("sensors"))
                {
                    var sensors = message["sensors"].AsBsonArray;
                    Console.WriteLine($"   Sensores: {sensors.Count}");
                    foreach (var sensor in sensors.Take(3))
                    {
                        if (sensor.IsBsonDocument)
                        {
                            var sensorDoc = sensor.AsBsonDocument;
                            Console.WriteLine($"     - {sensorDoc.GetValue("type", "")}: {sensorDoc.GetValue("value", 0)}");
                        }
                    }
                }
                Console.WriteLine();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro: {ex.Message}");
        }
    }

    static async Task ShowAggregatedData()
    {
        Console.WriteLine("\n=== DADOS AGREGADOS ===");
        try
        {
            var collection = database.GetCollection<BsonDocument>("AggregatedData");
            var filter = Builders<BsonDocument>.Filter.Empty;
            var sort = Builders<BsonDocument>.Sort.Descending("received_at");
            
            var data = await collection.Find(filter)
                .Sort(sort)
                .Limit(5)
                .ToListAsync();

            if (data.Count == 0)
            {
                Console.WriteLine("Nenhum dado agregado encontrado.");
                return;
            }

            foreach (var item in data)
            {
                Console.WriteLine($"📊 Agregador: {item.GetValue("agregador_id", "N/A")}");
                Console.WriteLine($"   Timestamp: {item.GetValue("timestamp", "N/A")}");
                Console.WriteLine($"   Mensagens: {item.GetValue("message_count", 0)}");
                Console.WriteLine($"   Recebido: {item.GetValue("received_at", DateTime.MinValue)}");
                Console.WriteLine();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro: {ex.Message}");
        }
    }

    static async Task ShowSystemLogs()
    {
        Console.WriteLine("\n=== LOGS DO SISTEMA ===");
        try
        {
            var collection = database.GetCollection<BsonDocument>("SystemLogs");
            var filter = Builders<BsonDocument>.Filter.Empty;
            var sort = Builders<BsonDocument>.Sort.Descending("timestamp");
            
            var logs = await collection.Find(filter)
                .Sort(sort)
                .Limit(10)
                .ToListAsync();

            if (logs.Count == 0)
            {
                Console.WriteLine("Nenhum log encontrado.");
                return;
            }

            foreach (var log in logs)
            {
                Console.WriteLine($"📝 {log.GetValue("component", "N/A")} - {log.GetValue("event_type", "N/A")}");
                Console.WriteLine($"   {log.GetValue("description", "N/A")}");
                Console.WriteLine($"   {log.GetValue("timestamp", DateTime.MinValue)}");
                Console.WriteLine();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro: {ex.Message}");
        }
    }

    static async Task ShowStatistics()
    {
        Console.WriteLine("\n=== ESTATÍSTICAS ===");
        try
        {
            var wavyCollection = database.GetCollection<BsonDocument>("WavyMessages");
            var aggregatedCollection = database.GetCollection<BsonDocument>("AggregatedData");
            var logsCollection = database.GetCollection<BsonDocument>("SystemLogs");

            var wavyCount = await wavyCollection.CountDocumentsAsync(new BsonDocument());
            var aggregatedCount = await aggregatedCollection.CountDocumentsAsync(new BsonDocument());
            var logsCount = await logsCollection.CountDocumentsAsync(new BsonDocument());

            Console.WriteLine($"📊 Total de mensagens Wavy: {wavyCount}");
            Console.WriteLine($"📊 Total de lotes agregados: {aggregatedCount}");
            Console.WriteLine($"📊 Total de logs: {logsCount}");

            // Unique wavy nodes
            if (wavyCount > 0)
            {
                var wavyIds = await wavyCollection.DistinctAsync<string>("wavy_id", new BsonDocument());
                var uniqueWavy = (await wavyIds.ToListAsync()).Count;
                Console.WriteLine($"🌊 Nós Wavy únicos: {uniqueWavy}");
            }

            // Unique aggregators
            if (wavyCount > 0)
            {
                var agrIds = await wavyCollection.DistinctAsync<string>("agregador_id", new BsonDocument());
                var uniqueAgr = (await agrIds.ToListAsync()).Count;
                Console.WriteLine($"🔗 Agregadores únicos: {uniqueAgr}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro: {ex.Message}");
        }
    }

    static async Task ShowRealtimeData()
    {
        Console.WriteLine("\n=== MONITORAMENTO EM TEMPO REAL ===");
        Console.WriteLine("Pressione qualquer tecla para parar...\n");

        var lastCount = 0L;
        while (!Console.KeyAvailable)
        {
            try
            {
                var collection = database.GetCollection<BsonDocument>("WavyMessages");
                var currentCount = await collection.CountDocumentsAsync(new BsonDocument());
                
                if (currentCount != lastCount)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Total de mensagens: {currentCount} (+{currentCount - lastCount})");
                    lastCount = currentCount;
                }

                await Task.Delay(2000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro no monitoramento: {ex.Message}");
                break;
            }
        }
        
        Console.ReadKey(); // Consume the key press
    }
}
