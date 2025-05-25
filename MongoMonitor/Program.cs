using Shared.MongoDB;
using MongoDB.Bson;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== MongoDB Monitor para Sistemas Distribuídos ===\n");

        try
        {
            var mongoService = new MongoDBService();
            
            // Test connection
            Console.WriteLine("Testando conexão com MongoDB...");
            var connected = await mongoService.TestConnectionAsync();
            
            if (!connected)
            {
                Console.WriteLine("Falha na conexão com MongoDB. Verifique a string de conexão.");
                return;
            }

            while (true)
            {
                Console.WriteLine("\n=== MENU ===");
                Console.WriteLine("1. Ver estatísticas gerais");
                Console.WriteLine("2. Ver mensagens recentes (últimas 10)");
                Console.WriteLine("3. Sair");
                Console.Write("Escolha uma opção: ");

                var choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        await ShowStatistics(mongoService);
                        break;
                    case "2":
                        await ShowRecentMessages(mongoService);
                        break;
                    case "3":
                        Console.WriteLine("Encerrando monitor...");
                        return;
                    default:
                        Console.WriteLine("Opção inválida. Tente novamente.");
                        break;
                }

                Console.WriteLine("\nPressione qualquer tecla para continuar...");
                Console.ReadKey();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro: {ex.Message}");
        }
    }

    static async Task ShowStatistics(MongoDBService mongoService)
    {
        Console.WriteLine("\n=== ESTATÍSTICAS ===");
        
        try
        {
            var stats = await mongoService.GetStatisticsAsync();
            
            Console.WriteLine($"Total de mensagens Wavy: {stats.GetValueOrDefault("total_wavy_messages", 0)}");
            Console.WriteLine($"Total de lotes agregados: {stats.GetValueOrDefault("total_aggregated_batches", 0)}");
            Console.WriteLine($"Nós Wavy únicos: {stats.GetValueOrDefault("unique_wavy_nodes", 0)}");
            Console.WriteLine($"Agregadores únicos: {stats.GetValueOrDefault("unique_aggregators", 0)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter estatísticas: {ex.Message}");
        }
    }

    static async Task ShowRecentMessages(MongoDBService mongoService)
    {
        Console.WriteLine("\n=== MENSAGENS RECENTES ===");
        
        try
        {
            var messages = await mongoService.GetRecentWavyMessagesAsync(10);
            
            if (messages.Count == 0)
            {
                Console.WriteLine("Nenhuma mensagem encontrada.");
                return;
            }

            foreach (var message in messages)
            {
                Console.WriteLine($"--- Mensagem ---");
                Console.WriteLine($"Wavy ID: {message.GetValue("wavy_id", "")}");
                Console.WriteLine($"Agregador ID: {message.GetValue("agregador_id", "")}");
                Console.WriteLine($"Timestamp: {message.GetValue("timestamp", "")}");
                Console.WriteLine($"Recebido em: {message.GetValue("received_at", DateTime.MinValue)}");
                
                if (message.Contains("sensors") && message["sensors"].IsBsonArray)
                {
                    var sensors = message["sensors"].AsBsonArray;
                    Console.WriteLine($"Sensores ({sensors.Count}):");
                    foreach (var sensor in sensors)
                    {
                        if (sensor.IsBsonDocument)
                        {
                            var sensorDoc = sensor.AsBsonDocument;
                            Console.WriteLine($"  - {sensorDoc.GetValue("type", "")}: {sensorDoc.GetValue("value", 0.0)}");
                        }
                    }
                }
                Console.WriteLine();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter mensagens recentes: {ex.Message}");
        }
    }
}
