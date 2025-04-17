using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

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



    static readonly ConcurrentQueue<string> dataQueue = new();
    static TcpClient? serverClient;
    static NetworkStream? serverStream;
    static volatile bool encerrarExecucao = false;

    static string aggregatorID = "";
    static string aggregatorRegion = "";
    static int aggregatorPort = 0;

    static void Main()
    {
        Console.Write("ID do Agregador: ");
        aggregatorID = Console.ReadLine()?.Trim() ?? "";
        if (string.IsNullOrEmpty(aggregatorID) || !aggregatorID.Contains('_'))
        {
            Console.WriteLine("ID inválido. O ID deve estar no formato <Região>_Agr (exemplo: N_Agr).");
            return;
        }
        aggregatorRegion = aggregatorID.Split('_')[0];

        // Lê o ficheiro de configuração para ver a port para este agregador
        if (!File.Exists(configAgrPath))
        {
            Console.WriteLine("Arquivo de configuração não encontrado: " + configAgrPath);
            return;
        }
        try
        {
            var lines = File.ReadAllLines(configAgrPath);
            foreach (var line in lines)
            {
                var parts = line.Split(',');
                if (parts.Length >= 2 && parts[0].Trim().Equals(aggregatorID, StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(parts[1].Trim(), out aggregatorPort) || aggregatorPort <= 0)
                    {
                        Console.WriteLine("Porta inválida configurada para " + aggregatorID);
                        return;
                    }
                    break;
                }
            }
            if (aggregatorPort <= 0)
            {
                Console.WriteLine($"Configuração para agregador {aggregatorID} não encontrada ou porta inválida.");
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Erro ao ler o arquivo de configuração: " + ex.Message);
            return;
        }

        Console.WriteLine($"[{aggregatorID}] Utilizando a porta {aggregatorPort} conforme arquivo de configuração.");
        Console.WriteLine($"[{aggregatorID}] A estabelecer conexão com o Servidor...");

        try
        {
            serverClient = new TcpClient("127.0.0.1", 11000);
            serverStream = serverClient.GetStream();
            Console.WriteLine($"[{aggregatorID}] Conexão estabelecida com o Servidor.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{aggregatorID}] Erro ao conectar ao Servidor: {ex.Message}");
            return;
        }

        var listener = new TcpListener(IPAddress.Any, aggregatorPort);
        listener.Start();
        Console.WriteLine($"[{aggregatorID}] A ouvir WAVYs na porta {aggregatorPort}...\n");

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
                    Console.WriteLine($"[{aggregatorID}] Erro ao aceitar conexão de WAVY: {ex.Message}");
                }
            }
        });

        // Processa dados e envia ao Servidor a cada 5 segundos
        Task.Run(() => ProcessAndSendData());

        // Monitorar comando de desligamento
        Task.Run(() => MonitorarComandoDesligar());

        // Mantém a main thread ativa até encerramento
        while (!encerrarExecucao)
        {
            Thread.Sleep(200);
        }

        Console.WriteLine($"[{aggregatorID}] Encerrando execução...");
        listener.Stop();

        // Fecha a conexão com o Servidor
        serverStream?.Close();
        serverClient?.Close();
        Console.WriteLine($"[{aggregatorID}] Conexão com o Servidor encerrada.");
    }

    static void HandleWavy(TcpClient wavyClient)
    {
        using var stream = wavyClient.GetStream();
        var buffer = new byte[4096];
        var received = stream.Read(buffer, 0, buffer.Length);
        var message = Encoding.UTF8.GetString(buffer, 0, received);

        // Verifica handshake inicial
        if (message.StartsWith("Liga"))
        {
            // Envia "OK" para prosseguir com o handshake
            var respostaLiga = Encoding.UTF8.GetBytes("OK");
            stream.Write(respostaLiga, 0, respostaLiga.Length);

            // Aguarda o ID enviado pela Wavy
            received = stream.Read(buffer, 0, buffer.Length);
            message = Encoding.UTF8.GetString(buffer, 0, received);
            if (message.StartsWith("ID:"))
            {
                var wavyID = message.Replace("ID:", "").Trim();
                // Verifica se a região da Wavy corresponde à do Agregador.
                if (!wavyID.Contains('_') || wavyID.Split('_')[0] != aggregatorRegion)
                {
                    Console.WriteLine($"[{aggregatorID}] Wavy de ID {wavyID} tem região incompatível e será rejeitada.");
                    wavyClient.Close();
                    return;
                }
                Console.WriteLine($"[{aggregatorID}] Conexão estabelecida com {wavyID}");
                var ack = Encoding.UTF8.GetBytes("ACK");
                stream.Write(ack, 0, ack.Length);
            }
            else
            {
                wavyClient.Close();
                return;
            }
        }
        else if (message.Trim().Equals("DLG"))
        {
            Console.WriteLine($"[{aggregatorID}] Requisição de desligamento recebida da WAVY.");
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
                Console.WriteLine($"[{aggregatorID}] Dados recebidos de {wavyId}");
            }
            catch
            {
                Console.WriteLine($"[{aggregatorID}] Dados recebidos de uma mensagem não formatada em JSON.");
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
                var modifiedMessages = new List<string>();

                while (dataQueue.TryDequeue(out var data))
                {
                    try
                    {
                        // Dá Parse em cada mensagem e insere o ID do Agregador
                        var jsonNode = JsonNode.Parse(data);
                        if (jsonNode is JsonObject obj)
                        {
                            obj["agregador_id"] = aggregatorID;
                            modifiedMessages.Add(obj.ToJsonString());
                        }
                        else
                        {
                            modifiedMessages.Add(data);
                        }
                    }
                    catch
                    {
                        modifiedMessages.Add(data); // caso falhe o parse, envia como está
                    }
                }

                // Agrega todas as mensagens numa única mensagem.
                var aggregatedMessage = string.Join("\n", modifiedMessages);
                SendDataToServer(aggregatedMessage);
            }
        }
    }

    static void SendDataToServer(string aggregatedData)
    {
        if (serverStream == null || serverClient == null || !serverClient.Connected)
        {
            Console.WriteLine($"[{aggregatorID}] Conexão com o Servidor não está ativa.");
            return;
        }

        try
        {
            Console.WriteLine($"[{aggregatorID}] Enviando dados para o Servidor...");
            var messageToSend = aggregatedData + "<|EOM|>";
            var dadosBytes = Encoding.UTF8.GetBytes(messageToSend);
            serverStream.Write(dadosBytes, 0, dadosBytes.Length);

            var ackBuffer = new byte[1024];
            var ackReceived = serverStream.Read(ackBuffer, 0, ackBuffer.Length);
            var ack = Encoding.UTF8.GetString(ackBuffer, 0, ackReceived);
            Console.WriteLine($"[{aggregatorID}] Resposta do Servidor: {ack}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{aggregatorID}] Erro ao enviar dados para o Servidor: {ex.Message}");
        }
    }

    static void MonitorarComandoDesligar()
    {
        while (!encerrarExecucao)
        {
            var comando = Console.ReadLine();
            if (comando != null && comando.Trim().Equals("DLG", System.StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[{aggregatorID}] Encerrando execução... Aguardando resposta do Servidor...");
                try
                {
                    if (serverStream != null && serverClient != null && serverClient.Connected)
                    {
                        var msg = Encoding.UTF8.GetBytes("Desliga");
                        serverStream.Write(msg, 0, msg.Length);

                        var ackBuffer = new byte[1024];
                        var ackReceived = serverStream.Read(ackBuffer, 0, ackBuffer.Length);
                        var ack = Encoding.UTF8.GetString(ackBuffer, 0, ackReceived);
                        Console.WriteLine($"[{aggregatorID}] Resposta do Servidor: {ack}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{aggregatorID}] Erro ao enviar desligamento ao Servidor: {ex.Message}");
                }

                encerrarExecucao = true;
            }
        }
    }
}