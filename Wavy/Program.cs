using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    // Path to the aggregator configuration CSV. Ensure the file exists.
    static readonly string configAgrPath = Path.GetFullPath(
    Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "..", "..", "..", "..",     // "sobe" até à raiz do projecto
        "Config",                   // "desce" para a pasta Config
        "config_agr.csv"
    )
);


    static void Main()
    {
        // Prompt for Wavy ID (e.g., "N_Wavy01", "S_Wavy01", "E_Wavy01", "W_Wavy01").
        Console.Write("ID da Wavy: ");
        string wavyID = Console.ReadLine()?.Trim() ?? "";
        if (string.IsNullOrEmpty(wavyID) || !wavyID.Contains('_'))
        {
            Console.WriteLine("ID inválido. O ID deve estar no formato <Região>_WavyXX (ex: N_Wavy01).");
            return;
        }

        // Determine the region based on the prefix (assumes format: <Region>_Wavy...)
        string wavyRegion = wavyID.Split('_')[0];

        // Read aggregator configuration to determine the corresponding aggregator port.
        int aggregatorPort = 0; // default
        try
        {
            if (!File.Exists(configAgrPath))
            {
                Console.WriteLine("Arquivo de configuração não encontrado: " + configAgrPath);
                return;
            }
            var lines = File.ReadAllLines(configAgrPath);
            foreach (var line in lines)
            {
                // Ignore header line if present
                if (line.StartsWith("aggregator_id", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = line.Split(',');
                if (parts.Length >= 2)
                {
                    var configID = parts[0].Trim();
                    // Expect aggregator IDs like "N_Agr", "S_Agr", etc.
                    if (configID.StartsWith(wavyRegion + "_", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(parts[1].Trim(), out aggregatorPort) && aggregatorPort > 0)
                        {
                            break;
                        }
                    }
                }
            }
            if (aggregatorPort <= 0)
            {
                Console.WriteLine($"Configuração para agregador da região {wavyRegion} não encontrada ou porta inválida.");
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Erro ao ler o arquivo de configuração: " + ex.Message);
            return;
        }

        Console.WriteLine($"[WAVY {wavyID}] Tentando conectar ao Agregador da região {wavyRegion} na porta {aggregatorPort}...");

        // Attempt to establish handshake with the aggregator on the designated port.
        bool conectado = false;
        while (!conectado)
        {
            try
            {
                using (var client = new TcpClient("127.0.0.1", aggregatorPort))
                using (var stream = client.GetStream())
                {
                    // Envia "LIga" para iniciar o handshake
                    var ligaBytes = Encoding.UTF8.GetBytes("LIga");
                    stream.Write(ligaBytes, 0, ligaBytes.Length);

                    var buffer = new byte[1024];
                    int received = stream.Read(buffer, 0, buffer.Length);
                    var response = Encoding.UTF8.GetString(buffer, 0, received);

                    if (response == "OK")
                    {
                        // Envia o ID para o handshake
                        var idMsg = Encoding.UTF8.GetBytes("ID:" + wavyID);
                        stream.Write(idMsg, 0, idMsg.Length);

                        received = stream.Read(buffer, 0, buffer.Length);
                        var response2 = Encoding.UTF8.GetString(buffer, 0, received);
                        if (response2 == "ACK")
                        {
                            conectado = true;
                            Console.WriteLine($"[WAVY {wavyID}] Conexão estabelecida com o Agregador.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WAVY {wavyID}] Erro ao conectar: {ex.Message}");
                Thread.Sleep(1000);
            }
        }

        // Task to send data periodically to the aggregator.
        Task.Run(() =>
        {
            var rnd = new Random();
            int segundos = 0;
            bool desligar = false;

            // Monitor shutdown command in a separate thread
            Task.Run(() =>
            {
                while (!desligar)
                {
                    var comando = Console.ReadLine();
                    if (comando != null && comando.Trim().Equals("DLG", StringComparison.OrdinalIgnoreCase))
                    {
                        desligar = true;
                        Console.WriteLine($"[WAVY {wavyID}] Terminando execução. Enviando pedido de desligamento...");
                        try
                        {
                            using (var client = new TcpClient("127.0.0.1", aggregatorPort))
                            using (var stream = client.GetStream())
                            {
                                var msg = Encoding.UTF8.GetBytes("DLG");
                                stream.Write(msg, 0, msg.Length);

                                var buffer = new byte[1024];
                                int received = stream.Read(buffer, 0, buffer.Length);
                                var response = Encoding.UTF8.GetString(buffer, 0, received);
                                Console.WriteLine($"[WAVY {wavyID}] Resposta do Agregador: {response}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[WAVY {wavyID}] Erro ao enviar desligamento: {ex.Message}");
                        }
                    }
                }
            });

            while (!desligar)
            {
                double temperatura = Math.Round(15 + rnd.NextDouble() * 10, 2);
                string jsonData;

                if (segundos % 2 == 0)
                {
                    double umidade = rnd.Next(0, 100);
                    var sensors = new[]
                    {
                        new { sensor_type = "temperature", value = temperatura },
                        new { sensor_type = "humidity", value = umidade }
                    };

                    jsonData = JsonSerializer.Serialize(new
                    {
                        wavy_id = wavyID,
                        sensors,
                        timestamp = DateTime.Now.ToString("o")
                    });
                }
                else
                {
                    var sensors = new[]
                    {
                        new { sensor_type = "temperature", value = temperatura }
                    };

                    jsonData = JsonSerializer.Serialize(new
                    {
                        wavy_id = wavyID,
                        sensors,
                        timestamp = DateTime.Now.ToString("o")
                    });
                }

                var mensagem = jsonData + "<|EOM|>";
                var data = Encoding.UTF8.GetBytes(mensagem);

                try
                {
                    using (var client = new TcpClient("127.0.0.1", aggregatorPort))
                    using (var stream = client.GetStream())
                    {
                        stream.Write(data, 0, data.Length);

                        var buffer = new byte[1024];
                        int received = stream.Read(buffer, 0, buffer.Length);
                        var response = Encoding.UTF8.GetString(buffer, 0, received);
                        Console.WriteLine($"[WAVY {wavyID}] Resposta do Agregador: {response}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WAVY {wavyID}] Erro ao enviar dados: {ex.Message}");
                }

                Thread.Sleep(1000);
                segundos++;
            }

            Console.WriteLine($"[WAVY {wavyID}] Encerrando execução.");
        }).Wait();
    }
}