using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

class Program
{
    static readonly string basePath = Path.Combine(
        Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)!.Parent!.Parent!.Parent!.FullName, "registos");

    static readonly Dictionary<string, Mutex> fileMutexes = new();

    static void Main()
    {
        Directory.CreateDirectory(basePath);
        var listener = new TcpListener(IPAddress.Any, 11000);
        listener.Start();
        Console.WriteLine("[SERVIDOR] A ouvir na porta 11000...");

        while (true)
        {
            var client = listener.AcceptTcpClient();
            new Thread(() => HandleClient(client)).Start();
        }
    }

    static void HandleClient(TcpClient client)
    {
        // Mensagem para indicar conexão recebida (tipo "A estabelecer conexão com o Agregador...")
        Console.WriteLine("[SERVIDOR] A estabelecer conexão com o Agregador...");

        using var stream = client.GetStream();
        Console.WriteLine("[SERVIDOR] Conexão estabelecida.");

        var buffer = new byte[4096];
        var received = stream.Read(buffer, 0, buffer.Length);
        var message = Encoding.UTF8.GetString(buffer, 0, received);
        message = message.Trim();

        // Se for "Desliga" do Agregador
        if (message.Equals("Desliga", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("[SERVIDOR] Agregador requisitou desligar.");
            var okMsg = Encoding.UTF8.GetBytes("<|OK|>");
            stream.Write(okMsg, 0, okMsg.Length);
            client.Close();
            return;
        }

        // Senão, processa dados com <|EOM|>
        if (message.Contains("<|EOM|>"))
        {
            message = message.Replace("<|EOM|>", "");
            Console.WriteLine($"[SERVIDOR] Dados recebidos: {message}");

            try
            {
                var doc = JsonDocument.Parse(message);
                var id = doc.RootElement.GetProperty("wavy_id").GetString();
                var filePath = Path.Combine(basePath, $"registos_{id}.json");

                lock (fileMutexes)
                {
                    if (!fileMutexes.ContainsKey(id))
                        fileMutexes[id] = new Mutex();
                }

                fileMutexes[id].WaitOne();
                File.AppendAllText(filePath, message + Environment.NewLine);
                fileMutexes[id].ReleaseMutex();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SERVIDOR] Erro ao guardar JSON: {ex.Message}");
            }

            var ack = Encoding.UTF8.GetBytes("<|ACK|>");
            stream.Write(ack, 0, ack.Length);
        }

        client.Close();
    }
}