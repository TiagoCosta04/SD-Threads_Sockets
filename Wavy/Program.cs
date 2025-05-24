using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Shared.Models;
using Shared.RabbitMQ;

class Program
{
    // Config da Wavy
    static readonly string configWavyPath = Path.GetFullPath(
        Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..",     // sobe para a raiz do projecto
            "Config",
            "config_wavy.csv"
        )
    );

    static RabbitMQRpcClient? rpcClient;
    static string wavyID = "";
    static string wavyRegion = "";
    static string rpcQueueName = "";
    static volatile bool encerrarExecucao = false;

    static async Task Main()
    {
        // Loop até que um ID válido seja fornecido
        while (true)
        {
            Console.Write("ID da Wavy: ");
            wavyID = Console.ReadLine()?.Trim() ?? "";
            if (string.IsNullOrEmpty(wavyID) || !wavyID.Contains('_'))
            {
                Console.WriteLine("ID inválido. O ID deve estar no formato <Região>_WavyXX (ex: N_Wavy01).");
                continue;
            }
            if (!IsWavyConfigured(wavyID))
            {
                Console.WriteLine($"Wavy {wavyID} não está configurada! Insira um ID válido.");
                continue;
            }

            // Status
            var status = GetWavyStatus(wavyID);
            if (status == "0")
            {
                Console.WriteLine($"{wavyID} Offline! Deseja voltar a ligá-la? (y/n) [y]: ");
                string input = Console.ReadLine()?.Trim().ToLower();
                // Se o input for y ou Enter
                if (string.IsNullOrEmpty(input) || input == "y")
                {
                    UpdateWavyStatus(wavyID, "1");
                    // Atualiza também o Timestamp
                    UpdateWavyLastSync(wavyID, DateTime.Now);
                    Console.WriteLine($"{wavyID} foi atualizado para Online.");
                }
                else
                {
                    // Volta a pedir o ID
                    continue;
                }
            }

            break;
        }

        // Determina a região da wavy
        wavyRegion = wavyID.Split('_')[0];
        rpcQueueName = RabbitMQConfig.RPC_QUEUE_PREFIX + wavyRegion;

        Console.WriteLine($"[{wavyID}] Inicializando RabbitMQ RPC Client...");

        try
        {
            rpcClient = new RabbitMQRpcClient();
            Console.WriteLine($"[{wavyID}] RabbitMQ configurado com sucesso.");
            Console.WriteLine($"[{wavyID}] RPC Queue: {rpcQueueName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{wavyID}] Erro ao configurar RabbitMQ: {ex.Message}");
            return;
        }

        // Estabelece handshake com o Agregador
        bool conectado = await EstabelecerHandshake();
        if (!conectado)
        {
            Console.WriteLine($"[{wavyID}] Falha ao estabelecer conexão com o Agregador.");
            return;
        }

        Console.WriteLine($"[{wavyID}] Conexão estabelecida com o Agregador via RabbitMQ RPC.");

        // Inicia o envio de dados
        var dataTask = Task.Run(async () => await EnviarDadosPeriodicamente());

        // Monitorar comando de desligamento
        var shutdownTask = Task.Run(async () => await MonitorarComandoDesligar());

        // Aguarda até que uma das tarefas complete
        await Task.WhenAny(dataTask, shutdownTask);

        Console.WriteLine($"[{wavyID}] Encerrando execução...");
        rpcClient?.Dispose();
        Console.WriteLine($"[{wavyID}] RabbitMQ resources cleaned up.");
    }

    static async Task<bool> EstabelecerHandshake()
    {
        if (rpcClient == null) return false;

        try
        {
            var request = new RpcRequest
            {
                Type = "HANDSHAKE",
                WavyId = wavyID
            };

            var response = await rpcClient.CallAsync(rpcQueueName, request, TimeSpan.FromSeconds(10));
            
            if (response.Status == "OK")
            {
                Console.WriteLine($"[{wavyID}] Handshake estabelecido: {response.Message}");
                return true;
            }
            else
            {
                Console.WriteLine($"[{wavyID}] Falha no handshake: {response.Message}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{wavyID}] Erro durante handshake: {ex.Message}");
            return false;
        }
    }

    static async Task EnviarDadosPeriodicamente()
    {
        var rnd = new Random();
        int segundos = 0;

        while (!encerrarExecucao && rpcClient != null)
        {
            try
            {
                double temperatura = Math.Round(15 + rnd.NextDouble() * 10, 2);
                
                WavyMessage wavyMessage;
                if (segundos % 2 == 0)
                {
                    double umidade = rnd.Next(0, 100);
                    wavyMessage = new WavyMessage
                    {
                        WavyId = wavyID,
                        Sensors = new[]
                        {
                            new SensorData { Type = "temperature", Value = temperatura },
                            new SensorData { Type = "humidity", Value = umidade }
                        },
                        Timestamp = DateTime.Now.ToString("o")
                    };
                }
                else
                {
                    wavyMessage = new WavyMessage
                    {
                        WavyId = wavyID,
                        Sensors = new[]
                        {
                            new SensorData { Type = "temperature", Value = temperatura }
                        },
                        Timestamp = DateTime.Now.ToString("o")
                    };
                }

                var request = new RpcRequest
                {
                    Type = "DATA",
                    WavyId = wavyID,
                    Data = JsonSerializer.Serialize(wavyMessage)
                };

                var response = await rpcClient.CallAsync(rpcQueueName, request, TimeSpan.FromSeconds(10));
                
                if (response.Status == "OK")
                {
                    Console.WriteLine($"[{wavyID}] Dados enviados com sucesso: {response.Message}");
                    UpdateWavyLastSync(wavyID, DateTime.Now);
                }
                else
                {
                    Console.WriteLine($"[{wavyID}] Erro ao enviar dados: {response.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{wavyID}] Erro ao enviar dados: {ex.Message}");
            }

            await Task.Delay(1000);
            segundos++;
        }
    }

    static async Task MonitorarComandoDesligar()
    {
        while (!encerrarExecucao)
        {
            var comando = Console.ReadLine();
            if (comando != null && comando.Trim().Equals("DLG", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[{wavyID}] Terminando execução. Enviando pedido de desligamento...");
                
                if (rpcClient != null)
                {
                    try
                    {
                        var request = new RpcRequest
                        {
                            Type = "SHUTDOWN",
                            WavyId = wavyID
                        };

                        var response = await rpcClient.CallAsync(rpcQueueName, request, TimeSpan.FromSeconds(10));
                        Console.WriteLine($"[{wavyID}] Resposta do Agregador: {response.Message}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{wavyID}] Erro ao enviar desligamento: {ex.Message}");
                    }
                }

                encerrarExecucao = true;
                break;
            }
        }
    }

    // Verifica se a wavy é configurada
    static bool IsWavyConfigured(string wavyID)
    {
        try
        {
            if (!File.Exists(configWavyPath))
                return false;
            var lines = File.ReadAllLines(configWavyPath);
            // Skip header line.
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(',');
                if (parts.Length >= 3)
                {
                    if (parts[0].Trim().Equals(wavyID, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch { }
        return false;
    }

    // Pega no status da wavy 0/1
    static string GetWavyStatus(string wavyID)
    {
        try
        {
            var lines = File.ReadAllLines(configWavyPath);
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(',');
                if (parts.Length >= 3)
                {
                    if (parts[0].Trim().Equals(wavyID, StringComparison.OrdinalIgnoreCase))
                        return parts[1].Trim();
                }
            }
        }
        catch { }
        return "0";
    }

    // Dá update no status da wavy
    static void UpdateWavyStatus(string wavyID, string newStatus)
    {
        try
        {
            var lines = File.ReadAllLines(configWavyPath).ToList();
            for (int i = 1; i < lines.Count; i++)
            {
                var parts = lines[i].Split(',');
                if (parts.Length >= 3 && parts[0].Trim().Equals(wavyID, StringComparison.OrdinalIgnoreCase))
                {
                    parts[1] = newStatus;
                    lines[i] = string.Join(",", parts);
                    break;
                }
            }
            File.WriteAllLines(configWavyPath, lines);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao atualizar status para {wavyID}: {ex.Message}");
        }
    }

    // Dá update no timestamp da wavy
    static void UpdateWavyLastSync(string wavyID, DateTime timestamp)
    {
        try
        {
            var lines = File.ReadAllLines(configWavyPath).ToList();
            for (int i = 1; i < lines.Count; i++)
            {
                var parts = lines[i].Split(',');
                if (parts.Length >= 3 && parts[0].Trim().Equals(wavyID, StringComparison.OrdinalIgnoreCase))
                {
                    parts[2] = timestamp.ToString("o");
                    lines[i] = string.Join(",", parts);
                    break;
                }
            }
            File.WriteAllLines(configWavyPath, lines);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao atualizar last_sync para {wavyID}: {ex.Message}");
        }
    }
}