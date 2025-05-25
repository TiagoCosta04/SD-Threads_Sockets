using Shared.MongoDB;
using Shared.Models;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Teste de Conexão MongoDB ===\n");

        try
        {
            var mongoService = new MongoDBService();
            
            // Test connection
            Console.WriteLine("1. Testando conexão com MongoDB...");
            var connected = await mongoService.TestConnectionAsync();
            
            if (!connected)
            {
                Console.WriteLine("❌ Falha na conexão com MongoDB.");
                return;
            }
            
            Console.WriteLine("✅ Conexão com MongoDB bem-sucedida!");

            // Test inserting a sample Wavy message
            Console.WriteLine("\n2. Testando inserção de mensagem Wavy...");
            var testMessage = new WavyMessage
            {
                WavyId = "TEST_WAVY01",
                AgregadorId = "TEST_AGR",
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Sensors = new[]
                {
                    new SensorData { Type = "temperature", Value = 25.5 },
                    new SensorData { Type = "pressure", Value = 1013.25 },
                    new SensorData { Type = "humidity", Value = 60.0 }
                }
            };

            await mongoService.InsertWavyMessageAsync(testMessage);
            Console.WriteLine("✅ Mensagem Wavy inserida com sucesso!");

            // Test inserting aggregated data
            Console.WriteLine("\n3. Testando inserção de dados agregados...");
            var testAggregatedData = new AggregatedData
            {
                AgregadorId = "TEST_AGR",
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Messages = new List<WavyMessage> { testMessage }
            };

            await mongoService.InsertAggregatedDataAsync(testAggregatedData);
            Console.WriteLine("✅ Dados agregados inseridos com sucesso!");

            // Test logging system event
            Console.WriteLine("\n4. Testando log de evento do sistema...");
            await mongoService.LogSystemEventAsync("TEST", "CONNECTION_TEST", "Teste de conexão executado com sucesso");
            Console.WriteLine("✅ Evento do sistema logado com sucesso!");

            // Test getting statistics
            Console.WriteLine("\n5. Obtendo estatísticas...");
            var stats = await mongoService.GetStatisticsAsync();
            Console.WriteLine($"   Total de mensagens Wavy: {stats.GetValueOrDefault("total_wavy_messages", 0)}");
            Console.WriteLine($"   Total de lotes agregados: {stats.GetValueOrDefault("total_aggregated_batches", 0)}");
            Console.WriteLine($"   Nós Wavy únicos: {stats.GetValueOrDefault("unique_wavy_nodes", 0)}");
            Console.WriteLine($"   Agregadores únicos: {stats.GetValueOrDefault("unique_aggregators", 0)}");

            Console.WriteLine("\n✅ Todos os testes concluídos com sucesso!");
            Console.WriteLine("\n📊 Pode agora verificar os dados no MongoDB Compass ou usar o MongoMonitor.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Erro durante o teste: {ex.Message}");
            Console.WriteLine($"Detalhes: {ex.StackTrace}");
        }

        Console.WriteLine("\nPressione qualquer tecla para sair...");
        Console.ReadKey();
    }
}
