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
    // Config da Wavy
    static readonly string configWavyPath = Path.GetFullPath(
        Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..",     // sobe para a raiz do projecto
            "Config",
            "config_wavy.csv"
        )
    );
    // Config do Agregador
    static readonly string configAgrPath = Path.GetFullPath(
        Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..",
            "Config",
            "config_agr.csv"
        )
    );

    static void Main()
    {
        // Loop até que um ID válido seja forneciado
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
        string wavyRegion = wavyID.Split('_')[0];

        // Lê a configuração do agregador para determinar que port se deve conectar
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
                // Ignora linha do header
                if (line.StartsWith("id", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = line.Split(',');
                if (parts.Length >= 2)
                {
                    var configID = parts[0].Trim();
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

        Console.WriteLine($"[{wavyID}] Tentando conectar ao Agregador da região {wavyRegion} na porta {aggregatorPort}...");

        // Tenta estabelecer conexão com o Agregador
        bool conectado = false;
        while (!conectado)
        {
            try
            {
                using (var client = new TcpClient("127.0.0.1", aggregatorPort))
                using (var stream = client.GetStream())
                {
                    // Send handshake initiation.
                    var ligaBytes = Encoding.UTF8.GetBytes("Liga");
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
                            Console.WriteLine($"[{wavyID}] Conexão estabelecida com o Agregador.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{wavyID}] Erro ao conectar: {ex.Message}");
                Thread.Sleep(1000);
            }
        }

        // Manda os dados periodicamente
        Task.Run(() =>
        {
            var rnd = new Random();
            int segundos = 0;
            bool desligar = false;

            // Verifica pelo comando para desligar numa thread em separado
            Task.Run(() =>
            {
                while (!desligar)
                {
                    var comando = Console.ReadLine();
                    if (comando != null && comando.Trim().Equals("DLG", StringComparison.OrdinalIgnoreCase))
                    {
                        desligar = true;
                        Console.WriteLine($"[{wavyID}] Terminando execução. Enviando pedido de desligamento...");
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
                                Console.WriteLine($"[{wavyID}] Resposta do Agregador: {response}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[{wavyID}] Erro ao enviar desligamento: {ex.Message}");
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
                        Console.WriteLine($"[{wavyID}] Resposta do Agregador: {response}");
                        UpdateWavyLastSync(wavyID, DateTime.Now);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{wavyID}] Erro ao enviar dados: {ex.Message}");
                }

                Thread.Sleep(1000);
                segundos++;
            }

            Console.WriteLine($"[{wavyID}] Encerrando execução.");
        }).Wait();
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