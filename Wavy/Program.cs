using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    static void Main()
    {
        Console.Write("ID da WAVY: ");
        var wavyId = Console.ReadLine();

        Console.WriteLine("[WAVY] Esperando estabelecer conexão com o Agregador...");

        bool conectado = false;
        while (!conectado)
        {
            try
            {
                using var client = new TcpClient("127.0.0.1", 11001);
                using var stream = client.GetStream();

                // Envia "LIga" para iniciar o handshake
                var ligaBytes = Encoding.UTF8.GetBytes("Liga");
                stream.Write(ligaBytes, 0, ligaBytes.Length);

                var buffer = new byte[1024];
                var received = stream.Read(buffer, 0, buffer.Length);
                var response = Encoding.UTF8.GetString(buffer, 0, received);

                if (response == "OK")
                {
                    // Envia o ID
                    var idMsg = Encoding.UTF8.GetBytes("ID:" + wavyId);
                    stream.Write(idMsg, 0, idMsg.Length);

                    received = stream.Read(buffer, 0, buffer.Length);
                    var response2 = Encoding.UTF8.GetString(buffer, 0, received);
                    if (response2 == "ACK")
                    {
                        conectado = true;
                        Console.WriteLine("[WAVY] Conexão estabelecida com o Agregador.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WAVY] Erro ao conectar: {ex.Message}");
                Task.Delay(1000).Wait();
            }
        }

        var rnd = new Random();
        int segundos = 0;
        bool desligar = false;

        // Task para monitorar comando de desligamento; escreva "DLG" para encerrar
        Task.Run(() =>
        {
            while (!desligar)
            {
                var comando = Console.ReadLine();
                if (comando != null && comando.Trim().Equals("DLG", System.StringComparison.OrdinalIgnoreCase))
                {
                    desligar = true;
                    Console.WriteLine("[WAVY] Terminando execução. Esperando resposta do Agregador...");
                    try
                    {
                        using var client = new TcpClient("127.0.0.1", 11001);
                        using var stream = client.GetStream();
                        var msg = Encoding.UTF8.GetBytes("DLG");
                        stream.Write(msg, 0, msg.Length);

                        var buffer = new byte[1024];
                        var received = stream.Read(buffer, 0, buffer.Length);
                        var response = Encoding.UTF8.GetString(buffer, 0, received);
                        Console.WriteLine($"[WAVY] Resposta do Agregador: {response}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WAVY] Erro ao enviar desligamento: {ex.Message}");
                    }
                }
            }
        });

        // Loop para envio periódico de dados
        while (!desligar)
        {
            double temperatura = Math.Round(15 + rnd.NextDouble() * 10, 2);
            string jsonData;

            if (segundos % 2 == 0)
            {
                double umidade = (double)rnd.Next(0, 100);
                var sensors = new[]
                {
                    new { sensor_type = "temperature", value = temperatura },
                    new { sensor_type = "humidity", value = umidade }
                };

                jsonData = JsonSerializer.Serialize(new
                {
                    wavy_id = wavyId,
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
                    wavy_id = wavyId,
                    sensors,
                    timestamp = DateTime.Now.ToString("o")
                });
            }

            var mensagem = jsonData + "<|EOM|>";
            var data = Encoding.UTF8.GetBytes(mensagem);

            try
            {
                using var client = new TcpClient("127.0.0.1", 11001);
                using var stream = client.GetStream();
                stream.Write(data, 0, data.Length);

                var buffer = new byte[1024];
                var received = stream.Read(buffer, 0, buffer.Length);
                var response = Encoding.UTF8.GetString(buffer, 0, received);
                Console.WriteLine($"[WAVY] Resposta do Agregador: {response}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WAVY] Erro: {ex.Message}");
            }

            Thread.Sleep(1000);
            segundos++;
        }

        Console.WriteLine($"[WAVY] {wavyId} terminando execução.");
    }
}