using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks;
using Shared.Models;
using Shared.RabbitMQ;

class Program
{
    // Caminho para o ficheiro de Config
    static readonly string configAgrPath = Path.GetFullPath(
        Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..",     // sobe até à raiz do projecto
            "Config",                   // desce para a pasta Config
            "config_agr.csv"            // nome do ficheiro
        )
    );

    static readonly ConcurrentQueue<WavyMessage> dataQueue = new();
    static RabbitMQRpcServer? rpcServer;
    static RabbitMQPublisher? publisher;
    static volatile bool encerrarExecucao = false;

    static string aggregatorID = "";
    static string aggregatorRegion = "";
    static string rpcQueueName = "";    static async Task Main()
    {
        Console.Write("ID do Agregador: ");
        aggregatorID = Console.ReadLine()?.Trim() ?? "";
        if (string.IsNullOrEmpty(aggregatorID) || !aggregatorID.Contains('_'))
        {
            Console.WriteLine("ID inválido. O ID deve estar no formato <Região>_Agr (exemplo: N_Agr).");
            return;
        }
        aggregatorRegion = aggregatorID.Split('_')[0];
        rpcQueueName = RabbitMQConfig.RPC_QUEUE_PREFIX + aggregatorRegion;

        // Verifica se o agregador está configurado
        if (!IsAggregatorConfigured(aggregatorID))
        {
            Console.WriteLine($"Agregador {aggregatorID} não está configurado no arquivo de configuração.");
            return;
        }

        Console.WriteLine($"[{aggregatorID}] Inicializando RabbitMQ components...");

        try
        {
            // Initialize RabbitMQ Publisher for sending data to Server
            publisher = new RabbitMQPublisher();
            Console.WriteLine($"[{aggregatorID}] RabbitMQ Publisher configurado com sucesso.");

            // Initialize RabbitMQ RPC Server for handling Wavy requests
            rpcServer = new RabbitMQRpcServer(rpcQueueName, HandleRpcRequest);
            rpcServer.Start();
            Console.WriteLine($"[{aggregatorID}] RabbitMQ RPC Server iniciado na queue: {rpcQueueName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{aggregatorID}] Erro ao configurar RabbitMQ: {ex.Message}");
            return;
        }

        Console.WriteLine($"[{aggregatorID}] Sistema iniciado com sucesso. A aguardar ligações de WAVYs...\n");

        // Processa dados e envia ao Servidor a cada 5 segundos
        var dataTask = Task.Run(async () => await ProcessAndSendData());

        // Monitorar comando de desligamento
        var shutdownTask = Task.Run(async () => await MonitorarComandoDesligar());

        // Aguarda até que uma das tarefas complete
        await Task.WhenAny(dataTask, shutdownTask);

        Console.WriteLine($"[{aggregatorID}] Encerrando execução...");
        
        // Cleanup resources
        rpcServer?.Dispose();
        publisher?.Dispose();
        Console.WriteLine($"[{aggregatorID}] RabbitMQ resources cleaned up.");
    }    static async Task<RpcResponse> HandleRpcRequest(RpcRequest request)
    {
        try
        {
            switch (request.Type?.ToUpper())
            {
                case "HANDSHAKE":
                    return HandleHandshake(request);
                
                case "DATA":
                    return HandleDataRequest(request);
                
                case "SHUTDOWN":
                    return HandleShutdownRequest(request);
                
                default:
                    return new RpcResponse
                    {
                        Status = "ERROR",
                        Message = "Tipo de request não reconhecido"
                    };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{aggregatorID}] Erro ao processar RPC request: {ex.Message}");
            return new RpcResponse
            {
                Status = "ERROR",
                Message = ex.Message
            };
        }
    }

    static RpcResponse HandleHandshake(RpcRequest request)
    {
        var wavyId = request.WavyId ?? "";
        
        // Verifica se a região da Wavy corresponde à do Agregador
        if (!wavyId.Contains('_') || wavyId.Split('_')[0] != aggregatorRegion)
        {
            Console.WriteLine($"[{aggregatorID}] Wavy {wavyId} tem região incompatível e será rejeitada.");
            return new RpcResponse
            {
                Status = "ERROR",
                Message = $"Região incompatível. Esperado: {aggregatorRegion}, Recebido: {wavyId.Split('_')[0]}"
            };
        }

        Console.WriteLine($"[{aggregatorID}] Handshake estabelecido com {wavyId}");
        return new RpcResponse
        {
            Status = "OK",
            Message = $"Conexão estabelecida com {aggregatorID}"
        };
    }

    static RpcResponse HandleDataRequest(RpcRequest request)
    {
        try
        {
            var wavyId = request.WavyId ?? "";
            var data = request.Data ?? "";

            if (string.IsNullOrEmpty(data))
            {
                return new RpcResponse
                {
                    Status = "ERROR",
                    Message = "Dados não fornecidos"
                };
            }

            // Parse the WavyMessage from JSON
            var wavyMessage = JsonSerializer.Deserialize<WavyMessage>(data);
            if (wavyMessage == null)
            {
                return new RpcResponse
                {
                    Status = "ERROR",
                    Message = "Falha ao deserializar dados"
                };
            }

            // Add aggregator ID to the message
            wavyMessage.AgregadorId = aggregatorID;

            // Add to processing queue
            dataQueue.Enqueue(wavyMessage);

            Console.WriteLine($"[{aggregatorID}] Dados recebidos de {wavyId} - {wavyMessage.Sensors.Length} sensores");
            
            return new RpcResponse
            {
                Status = "OK",
                Message = "Dados recebidos com sucesso"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{aggregatorID}] Erro ao processar dados: {ex.Message}");
            return new RpcResponse
            {
                Status = "ERROR",
                Message = $"Erro ao processar dados: {ex.Message}"
            };
        }
    }

    static RpcResponse HandleShutdownRequest(RpcRequest request)
    {
        var wavyId = request.WavyId ?? "";
        Console.WriteLine($"[{aggregatorID}] Pedido de desligamento recebido de {wavyId}");
        
        return new RpcResponse
        {
            Status = "OK",
            Message = $"Desligamento de {wavyId} reconhecido"
        };
    }    static async Task ProcessAndSendData()
    {
        while (!encerrarExecucao)
        {
            await Task.Delay(5000);

            if (!dataQueue.IsEmpty)
            {
                var messages = new List<WavyMessage>();

                // Collect all pending messages
                while (dataQueue.TryDequeue(out var message))
                {
                    messages.Add(message);
                }

                if (messages.Count > 0)
                {
                    // Create aggregated data
                    var aggregatedData = new AggregatedData
                    {
                        AgregadorId = aggregatorID,
                        Messages = messages,
                        Timestamp = DateTime.Now.ToString("o")
                    };

                    // Send to server via RabbitMQ
                    SendDataToServer(aggregatedData);
                }
            }
        }
    }

    static void SendDataToServer(AggregatedData aggregatedData)
    {
        if (publisher == null)
        {
            Console.WriteLine($"[{aggregatorID}] RabbitMQ Publisher não está disponível.");
            return;
        }

        try
        {
            Console.WriteLine($"[{aggregatorID}] Enviando {aggregatedData.Messages.Count} mensagens para o Servidor via RabbitMQ...");
            publisher.PublishData(aggregatedData);
            Console.WriteLine($"[{aggregatorID}] Dados enviados com sucesso para o Servidor.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{aggregatorID}] Erro ao enviar dados para o Servidor: {ex.Message}");
        }
    }

    static async Task MonitorarComandoDesligar()
    {
        while (!encerrarExecucao)
        {
            var comando = Console.ReadLine();
            if (comando != null && comando.Trim().Equals("DLG", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[{aggregatorID}] Comando de desligamento recebido...");
                
                // Send shutdown notification to server
                if (publisher != null)
                {
                    try
                    {
                        publisher.PublishShutdown(aggregatorID);
                        Console.WriteLine($"[{aggregatorID}] Notificação de shutdown enviada ao Servidor.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{aggregatorID}] Erro ao enviar notificação de shutdown: {ex.Message}");
                    }
                }

                encerrarExecucao = true;
                break;
            }

            await Task.Delay(100);
        }
    }

    static bool IsAggregatorConfigured(string aggregatorID)
    {
        try
        {
            if (!File.Exists(configAgrPath))
                return false;
            
            var lines = File.ReadAllLines(configAgrPath);
            foreach (var line in lines.Skip(1)) // Skip header
            {
                var parts = line.Split(',');
                if (parts.Length >= 2 && parts[0].Trim().Equals(aggregatorID, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch { }
        return false;
    }
}