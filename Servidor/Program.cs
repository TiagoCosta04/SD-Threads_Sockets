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
            Console.WriteLine("[SERVIDOR] Conexão estabelecida com o Agregador.");
            new Thread(() => HandleClient(client)).Start();
        }
    }

    static void HandleClient(TcpClient client)
    {
        using var stream = client.GetStream();
        while (true)
        {
            try
            {
                var buffer = new byte[4096];
                var received = stream.Read(buffer, 0, buffer.Length);
                if (received == 0) break;

                var message = Encoding.UTF8.GetString(buffer, 0, received).Trim();

                if (message == "Desliga")
                {
                    Console.WriteLine("[SERVIDOR] Agregador requisitou desligar.");
                    var okMsg = Encoding.UTF8.GetBytes("<|OK|>");
                    stream.Write(okMsg, 0, okMsg.Length);
                    break;
                }

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
            }
            catch (Exception)
            {
                break;
            }
        }

        client.Close();
        Console.WriteLine("[SERVIDOR] Conexão encerrada.");
    }
}