using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    // Paths for configuration files
    // Agregador configuration
    static readonly string configAgrPath = Path.GetFullPath(
        Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..",  // sobe para a raiz do projecto
            "Config",
            "config_agr.csv"
        )
    );
    // Wavy configuration
    static readonly string configWavyPath = Path.GetFullPath(
        Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..",  // sobe para a raiz do projecto
            "Config",
            "config_wavy.csv"
        )
    );

    static void Main()
    {
        // Loop until a valid, configured and online Wavy ID is provided.
        string wavyID = "";
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

            // Check status.
            var status = GetWavyStatus(wavyID);
            if (status == "0")
            {
                Console.WriteLine($"{wavyID} Offline! Deseja voltar a ligá-la? (y/n) [y]: ");
                string input = Console.ReadLine()?.Trim().ToLower();
                // If input is empty or "y", then update status to 1.
                if (string.IsNullOrEmpty(input) || input == "y")
                {
                    UpdateWavyStatus(wavyID, "1");
                    // Also update last_sync to current timestamp.
                    UpdateWavyLastSync(wavyID, DateTime.Now);
                    Console.WriteLine($"{wavyID} foi atualizado para Online.");
                }
                else
                {
                    // Ask for a new ID.
                    continue;
                }
            }

            // If status is "1", then proceed.
            break;
        }

        // Determine region from wavyID.
        string wavyRegion = wavyID.Split('_')[0];

        // Read aggregator configuration to determine corresponding aggregator port.
        int aggregatorPort = 0;
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
                // Ignore header line if present.
                if (line.StartsWith("id", StringComparison.OrdinalIgnoreCase)) continue;
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
            Console.WriteLine("Erro ao ler o arquivo de configuração do agregador: " + ex.Message);
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
                    // Send handshake initiation.
                    var ligaBytes = Encoding.UTF8.GetBytes("LIga");
                    stream.Write(ligaBytes, 0, ligaBytes.Length);

                    var buffer = new byte[1024];
                    int received = stream.Read(buffer, 0, buffer.Length);
                    var response = Encoding.UTF8.GetString(buffer, 0, received);

                    if (response == "OK")
                    {
                        // Send own ID.
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

            // Monitor shutdown command in a separate thread.
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
                        new { type = "temperature", value = temperatura },
                        new { type = "humidity", value = umidade }
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
                        new { type = "temperature", value = temperatura }
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
                        // Upon successful sync, update last_sync in the config.
                        UpdateWavyLastSync(wavyID, DateTime.Now);
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

    // Checks if the wavyID exists in the config file.
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

    // Gets the status ("1" online or "0" offline) of the Wavy in the config file.
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

    // Updates the status of a given Wavy in the configuration file.
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

    // Updates the last_sync field for the given Wavy.
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