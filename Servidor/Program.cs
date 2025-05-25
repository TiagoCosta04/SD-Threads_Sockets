using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using Shared.Models;
using Shared.RabbitMQ;
using Shared.MongoDB;

class Program
{
    static readonly string basePath = Path.Combine(
        Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)!.Parent!.Parent!.Parent!.FullName, "registos");    static readonly Dictionary<string, Mutex> fileMutexes = new();
    static RabbitMQSubscriber? subscriber;
    static MongoDBService? mongoService;
    static volatile bool encerrarExecucao = false;    static async Task Main()
    {
        Directory.CreateDirectory(basePath);
        
        // Initialize MongoDB
        Console.WriteLine("[SERVIDOR] Inicializando MongoDB...");
        try
        {
            mongoService = new MongoDBService();
            var connectionTest = await mongoService.TestConnectionAsync();
            if (!connectionTest)
            {
                Console.WriteLine("[SERVIDOR] Falha na conexão com MongoDB. Continuando apenas com arquivos locais.");
                mongoService = null;
            }
            else
            {
                Console.WriteLine("[SERVIDOR] MongoDB conectado com sucesso!");
                await mongoService.LogSystemEventAsync("SERVIDOR", "STARTUP", "Servidor iniciado com sucesso");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SERVIDOR] Erro ao conectar com MongoDB: {ex.Message}. Continuando apenas com arquivos locais.");
            mongoService = null;
        }
        
        Console.WriteLine("[SERVIDOR] Inicializando RabbitMQ Subscriber...");

        try
        {
            subscriber = new RabbitMQSubscriber("server_queue");
            
            // Subscribe to data messages
            subscriber.SubscribeToData(OnDataReceived);
            
            // Subscribe to shutdown messages
            subscriber.SubscribeToShutdown(OnShutdownReceived);
            
            Console.WriteLine("[SERVIDOR] RabbitMQ configurado com sucesso.");
            Console.WriteLine("[SERVIDOR] A ouvir mensagens de dados e shutdown via RabbitMQ...\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SERVIDOR] Erro ao configurar RabbitMQ: {ex.Message}");
            return;
        }

        // Monitorar comando de desligamento do console
        Task.Run(() => MonitorarComandoDesligar());

        // Mantém a aplicação em execução
        while (!encerrarExecucao)
        {
            await Task.Delay(200);
        }        Console.WriteLine("[SERVIDOR] Encerrando execução...");
        
        // Log shutdown event
        if (mongoService != null)
        {
            try
            {
                await mongoService.LogSystemEventAsync("SERVIDOR", "SHUTDOWN", "Servidor encerrando");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SERVIDOR] Erro ao logar shutdown no MongoDB: {ex.Message}");
            }
        }
        
        subscriber?.Dispose();
        Console.WriteLine("[SERVIDOR] RabbitMQ resources cleaned up.");
    }    static async void OnDataReceived(string message)
    {
        try
        {
            var aggregatedData = JsonSerializer.Deserialize<AggregatedData>(message);
            if (aggregatedData == null) return;

            Console.WriteLine($"[SERVIDOR] Dados recebidos de [{aggregatedData.AgregadorId}] - {aggregatedData.Messages.Count} mensagens");

            // Save to MongoDB if available
            if (mongoService != null)
            {
                try
                {
                    await mongoService.InsertAggregatedDataAsync(aggregatedData);
                    
                    // Also save individual wavy messages
                    foreach (var wavyMessage in aggregatedData.Messages)
                    {
                        await mongoService.InsertWavyMessageAsync(wavyMessage);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SERVIDOR] Erro ao salvar no MongoDB: {ex.Message}");
                }
            }

            // Continue saving to local files (backup)
            foreach (var wavyMessage in aggregatedData.Messages)
            {
                try
                {
                    var id = wavyMessage.WavyId;
                    var filePath = Path.Combine(basePath, $"registos_{id}.json");

                    lock (fileMutexes)
                    {
                        if (!fileMutexes.ContainsKey(id))
                            fileMutexes[id] = new Mutex();
                    }

                    fileMutexes[id].WaitOne();
                    var jsonString = JsonSerializer.Serialize(wavyMessage);
                    File.AppendAllText(filePath, jsonString + Environment.NewLine);
                    fileMutexes[id].ReleaseMutex();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SERVIDOR] Erro ao guardar dados de {wavyMessage.WavyId}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SERVIDOR] Erro ao processar dados recebidos: {ex.Message}");
        }
    }

    static void OnShutdownReceived(string message)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(message);
            var aggregatorId = jsonDoc.RootElement.GetProperty("AggregatorId").GetString();
            var timestamp = jsonDoc.RootElement.GetProperty("Timestamp").GetString();
            
            Console.WriteLine($"[SERVIDOR] Notificação de shutdown recebida de {aggregatorId} em {timestamp}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SERVIDOR] Erro ao processar shutdown: {ex.Message}");
        }
    }

    static void MonitorarComandoDesligar()
    {
        while (!encerrarExecucao)
        {
            var comando = Console.ReadLine();
            if (comando != null && comando.Trim().Equals("DLG", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[SERVIDOR] Comando de desligamento recebido...");
                encerrarExecucao = true;
                break;
            }
        }
    }
}