using System.Net.Sockets;
using System.Text;
using System.Text.Json;

class Program
{
    static void Main()
    {
        Console.Write("ID da WAVY: ");
        var wavyId = Console.ReadLine();

        // Antes de iniciar o envio de dados, espera o Agregador "ligar" ao Servidor.
        // Aqui vamos simular com tentativas de conexão para receber "OK" do Agregador
        Console.WriteLine("[WAVY] Esperando estabelecer conexão com o Agregador...");

        // Passo "Liga" para o Agregador
        var ligado = false;
        while (!ligado)
        {
            try
            {
                using var client = new TcpClient("127.0.0.1", 11001);
                using var stream = client.GetStream();

                // Envia "LIga"
                var ligaBytes = Encoding.UTF8.GetBytes("LIga");
                stream.Write(ligaBytes, 0, ligaBytes.Length);

                // Lê resposta "OK"
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
                        ligado = true;
                        Console.WriteLine("[WAVY] Conexão estabelecida com o Agregador.");
                    }
                }
            }
            catch
            {
                // Tenta novamente após breve intervalo
                Thread.Sleep(1000);
            }
        }

        var rnd = new Random();
        int segundos = 0;

        // Task para monitorar desligamento
        var desligar = false;
        Task.Run(() =>
        {
            while (!desligar)
            {
                var comando = Console.ReadLine();
                if (comando != null && comando.Trim().Equals("DLG", StringComparison.OrdinalIgnoreCase))
                {
                    desligar = true;
                    Console.WriteLine("[WAVY] Terminando execução. Esperando resposta do Agregador...");
                    // Solicitar "Desliga" ao Agregador
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

        // Loop de envio de dados (lógica atual)
        while (!desligar)
        {
            // Gerar dados de temperatura
            var temperatura = Math.Round(15 + rnd.NextDouble() * 10, 2);
            string jsonData;

            if (segundos % 2 == 0)
            {
                var umidade = rnd.Next(0, 100);
                var sensors = new List<object>
                {
                    new { sensor_type = "temperature", value = temperatura },
                    new { sensor_type = "humidity", value = umidade }
                };

                jsonData = JsonSerializer.Serialize(new
                {
                    wavy_id = wavyId,
                    sensors = sensors,
                    timestamp = DateTime.Now.ToString("o")
                });
            }
            else
            {
                var sensors = new List<object>
                {
                    new { sensor_type = "temperature", value = temperatura }
                };

                jsonData = JsonSerializer.Serialize(new
                {
                    wavy_id = wavyId,
                    sensors = sensors,
                    timestamp = DateTime.Now.ToString("o")
                });
            }

            var message = jsonData + "<|EOM|>";
            var data = Encoding.UTF8.GetBytes(message);

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