using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    static readonly string configDadosPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\config_dados.csv");
    static readonly ConcurrentQueue<string> dataQueue = new();
    static TcpClient? serverClient;
    static NetworkStream? serverStream;
    static volatile bool encerrarExecucao = false;

    static void Main()
    {
        Console.WriteLine("[AGREGADOR] A estabelecer conexão com o Servidor...");

        try
        {
            serverClient = new TcpClient("127.0.0.1", 11000);
            serverStream = serverClient.GetStream();
            Console.WriteLine("[AGREGADOR] Conexão estabelecida com o Servidor.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AGREGADOR] Erro ao conectar ao Servidor: {ex.Message}");
            return;
        }

        var listener = new TcpListener(IPAddress.Any, 11001);
        listener.Start();
        Console.WriteLine("[AGREGADOR] A ouvir WAVYs na porta 11001...");

        // Aceita conexões de WAVYs de forma bloqueante
        Task.Run(() =>
        {
            while (!encerrarExecucao)
            {
                try
                {
                    var wavyClient = listener.AcceptTcpClient();
                    new Thread(() => HandleWavy(wavyClient)).Start();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AGREGADOR] Erro ao aceitar conexão de WAVY: {ex.Message}");
                }
            }
        });

        // Task que processa dados e envia ao Servidor a cada 5 segundos
        Task.Run(() => ProcessAndSendData());

        // Task para monitorar comando de desligamento
        Task.Run(() => MonitorarComandoDesligar());

        // Mantém o main thread ativo até encerramento
        while (!encerrarExecucao)
        {
            Thread.Sleep(200);
        }

        Console.WriteLine("[AGREGADOR] Encerrando execução...");
        listener.Stop();

        // Fecha a conexão com o Servidor
        serverStream?.Close();
        serverClient?.Close();
        Console.WriteLine("[AGREGADOR] Conexão com o Servidor encerrada.");
    }

    static void HandleWavy(TcpClient wavyClient)
    {
        using var stream = wavyClient.GetStream();
        var buffer = new byte[4096];
        var received = stream.Read(buffer, 0, buffer.Length);
        var message = Encoding.UTF8.GetString(buffer, 0, received);

        // Verifica handshake inicial
        if (message.StartsWith("LIga"))
        {
            // Envia OK para sinalizar que o handshake pode prosseguir
            var respostaLiga = Encoding.UTF8.GetBytes("OK");
            stream.Write(respostaLiga, 0, respostaLiga.Length);

            // Aguarda o ID enviado pela Wavy
            received = stream.Read(buffer, 0, buffer.Length);
            message = Encoding.UTF8.GetString(buffer, 0, received);
            if (message.StartsWith("ID:"))
            {
                var wavyId = message.Replace("ID:", "").Trim();
                Console.WriteLine($"[AGREGADOR] Conexão estabelecida com {wavyId}");
                var ack = Encoding.UTF8.GetBytes("ACK");
                stream.Write(ack, 0, ack.Length);
            }
            else
            {
                wavyClient.Close();
                return;
            }
            // Após handshake, o cliente deverá enviar dados usando conexões separadas.
        }
        else if (message.Trim().Equals("DLG"))
        {
            Console.WriteLine("[AGREGADOR] Requisição de desligamento recebida da WAVY.");
            var resposta = Encoding.UTF8.GetBytes("<|OK|>");
            stream.Write(resposta, 0, resposta.Length);
            wavyClient.Close();
            return;
        }
        // Se for envio de dados
        else if (!string.IsNullOrEmpty(message) && message.Contains("<|EOM|>"))
        {
            message = message.Replace("<|EOM|>", "");
            try
            {
                using var jsonDoc = JsonDocument.Parse(message);
                var wavyId = jsonDoc.RootElement.GetProperty("wavy_id").GetString();
                Console.WriteLine($"[AGREGADOR] Dados recebidos de {wavyId}");
            }
            catch
            {
                Console.WriteLine("[AGREGADOR] Dados recebidos de uma mensagem não formatada em JSON.");
            }

            dataQueue.Enqueue(message);

            var resposta = Encoding.UTF8.GetBytes("<|OK|>");
            stream.Write(resposta, 0, resposta.Length);
        }

        wavyClient.Close();
    }

    static void ProcessAndSendData()
    {
        while (!encerrarExecucao)
        {
            Thread.Sleep(5000);

            if (!dataQueue.IsEmpty)
            {
                var dataToSend = new List<string>();

                while (dataQueue.TryDequeue(out var data))
                {
                    dataToSend.Add(data);
                }

                // Combine all queued messages into one aggregated message.
                // Using newline separator.
                var aggregatedMessage = string.Join("\n", dataToSend);
                SendDataToServer(aggregatedMessage);
            }
        }
    }

    static void SendDataToServer(string aggregatedData)
    {
        if (serverStream == null || serverClient == null || !serverClient.Connected)
        {
            Console.WriteLine("[AGREGADOR] Conexão com o Servidor não está ativa.");
            return;
        }

        try
        {
            Console.WriteLine("[AGREGADOR] Enviando dados para o Servidor...");
            var messageToSend = aggregatedData + "<|EOM|>";
            var dadosBytes = Encoding.UTF8.GetBytes(messageToSend);
            serverStream.Write(dadosBytes, 0, dadosBytes.Length);

            var ackBuffer = new byte[1024];
            var ackReceived = serverStream.Read(ackBuffer, 0, ackBuffer.Length);
            var ack = Encoding.UTF8.GetString(ackBuffer, 0, ackReceived);
            Console.WriteLine($"[AGREGADOR] Resposta do Servidor: {ack}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AGREGADOR] Erro ao enviar dados para o Servidor: {ex.Message}");
        }
    }

    static void MonitorarComandoDesligar()
    {
        while (!encerrarExecucao)
        {
            var comando = Console.ReadLine();
            if (comando != null && comando.Trim().Equals("DLG", System.StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[AGREGADOR] Encerrando execução... Aguardando resposta do Servidor...");

                try
                {
                    if (serverStream != null && serverClient != null && serverClient.Connected)
                    {
                        var msg = Encoding.UTF8.GetBytes("Desliga");
                        serverStream.Write(msg, 0, msg.Length);

                        var ackBuffer = new byte[1024];
                        var ackReceived = serverStream.Read(ackBuffer, 0, ackBuffer.Length);
                        var ack = Encoding.UTF8.GetString(ackBuffer, 0, ackReceived);
                        Console.WriteLine($"[AGREGADOR] Resposta do Servidor: {ack}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AGREGADOR] Erro ao enviar desligamento ao Servidor: {ex.Message}");
                }

                encerrarExecucao = true;
            }
        }
    }
}