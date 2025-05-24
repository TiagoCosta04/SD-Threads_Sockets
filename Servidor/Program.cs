using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using Shared.Models;
using Shared.RabbitMQ;

class Program
{
    static readonly string basePath = Path.Combine(
        Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)!.Parent!.Parent!.Parent!.FullName, "registos");

    static readonly Dictionary<string, Mutex> fileMutexes = new();
    static RabbitMQSubscriber? subscriber;
    static volatile bool encerrarExecucao = false;

    static async Task Main()
    {
        Directory.CreateDirectory(basePath);
        
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
        }

        Console.WriteLine("[SERVIDOR] Encerrando execução...");
        subscriber?.Dispose();
        Console.WriteLine("[SERVIDOR] RabbitMQ resources cleaned up.");
    }

    static void OnDataReceived(string message)
    {
        try
        {
            var aggregatedData = JsonSerializer.Deserialize<AggregatedData>(message);
            if (aggregatedData == null) return;

            Console.WriteLine($"[SERVIDOR] Dados recebidos de [{aggregatedData.AgregadorId}] - {aggregatedData.Messages.Count} mensagens");

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