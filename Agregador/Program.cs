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
    static readonly object lockObj = new();

    static bool servidorConectado = false;
    static volatile bool encerrarExecucao = false;

    static void Main()
    {
        Console.WriteLine("[AGREGADOR] A estabelecer conexão com o Servidor...");

        // Exemplo de tentativa de conexão inicial com o Servidor
        // (mantém a lógica atual de envio dados, mas adiciona etapa "Liga")
        try
        {
            using var testConnection = new TcpClient("127.0.0.1", 11000);
            Console.WriteLine("[AGREGADOR] Conexão estabelecida com o Servidor.");
            servidorConectado = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AGREGADOR] Erro ao estabelecer conexão inicial com o Servidor: {ex.Message}");
            // Dependendo da necessidade, podemos encerrar ou continuar tentando em loop
            // Para simplificar, assumiremos que é apenas um teste de conexão.
        }

        // Inicia a escuta do Agregador para WAVYs somente se servidor está conectado
        if (servidorConectado)
        {
            var listener = new TcpListener(IPAddress.Any, 11001);
            listener.Start();
            Console.WriteLine("[AGREGADOR] A ouvir WAVYs na porta 11001...");

            // Task que processa dados e envia ao Servidor
            Task.Run(() => ProcessAndSendData());

            // Task opcional para ouvir comando de desligar
            Task.Run(() => MonitorarComandoDesligar());

            while (!encerrarExecucao)
            {
                if (listener.Pending())
                {
                    var wavyClient = listener.AcceptTcpClient();
                    new Thread(() => HandleWavy(wavyClient)).Start();
                }
                Thread.Sleep(200);
            }
        }

        Console.WriteLine("[AGREGADOR] Execução finalizada.");
    }

    static void HandleWavy(TcpClient wavyClient)
    {
        // Quando Wavy conectar, podemos exibir a mensagem de conexão
        // "Conexão estabelecida com {wavyId}" somente após lermos o ID
        using var stream = wavyClient.GetStream();
        var buffer = new byte[4096];
        var received = stream.Read(buffer, 0, buffer.Length);
        var message = Encoding.UTF8.GetString(buffer, 0, received);

        // Exemplo simples: Se a mensagem começar por "LIga", mandar "OK"
        if (message.StartsWith("LIga"))
        {
            var respostaLiga = Encoding.UTF8.GetBytes("OK");
            stream.Write(respostaLiga, 0, respostaLiga.Length);

            // Em seguida, aguarda ID
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
                // Se não receber ID adequado, fecha a conexão
                wavyClient.Close();
                return;
            }

            // Após ID, Wavy deve enviar dados normais contendo <|EOM|> etc.
            // (esse trecho é simplificado; cada parte do fluxo pode ser tratada em while)
            received = stream.Read(buffer, 0, buffer.Length);
            message = Encoding.UTF8.GetString(buffer, 0, received);
        }

        // Se for solicitado "Desliga" — no caso da Wavy querer encerrar
        if (message.Trim().Equals("DLG"))
        {
            Console.WriteLine("[AGREGADOR] Wavy requisitou desligar.");
            var resposta = Encoding.UTF8.GetBytes("<|OK|>");
            stream.Write(resposta, 0, resposta.Length);
            wavyClient.Close();
            return;
        }

        // Lógica de recebimento de dados como já existia
        if (!string.IsNullOrEmpty(message) && message.Contains("<|EOM|>"))
        {
            message = message.Replace("<|EOM|>", "");
            Console.WriteLine($"[AGREGADOR] Mensagem recebida da WAVY: {message}");

            // Adiciona os dados recebidos na fila
            dataQueue.Enqueue(message);

            // Responder à WAVY
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

                SendDataToServer(dataToSend);
            }
        }
    }

    static void SendDataToServer(List<string> dataBatch)
    {
        // Mantém a lógica atual de envio
        try
        {
            var configs = File.ReadAllLines(configDadosPath).Skip(1)
                .Select(l => l.Split(','))
                .ToDictionary(l => l[0], l => new { PreProc = l[1], Volume = int.Parse(l[2]), Destino = l[3] });

            foreach (var data in dataBatch)
            {
                using var doc = JsonDocument.Parse(data);
                var json = doc.RootElement;
                var wavyId = json.GetProperty("wavy_id").GetString();

                if (!configs.ContainsKey(wavyId))
                {
                    Console.WriteLine($"[AGREGADOR] Sem configuração para WAVY {wavyId}. Ignorado.");
                    continue;
                }

                var config = configs[wavyId];
                var partes = config.Destino.Split(':');
                var ip = partes[0];
                var porta = int.Parse(partes[1]);

                using var server = new TcpClient(ip, porta);
                using var serverStream = server.GetStream();
                var dadosBytes = Encoding.UTF8.GetBytes(data + "<|EOM|>");
                serverStream.Write(dadosBytes, 0, dadosBytes.Length);

                var ackBuffer = new byte[1024];
                var ackReceived = serverStream.Read(ackBuffer, 0, ackBuffer.Length);
                var ack = Encoding.UTF8.GetString(ackBuffer, 0, ackReceived);

                Console.WriteLine($"[AGREGADOR] Resposta do servidor: {ack}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AGREGADOR] Erro ao enviar para servidor: {ex.Message}");
        }
    }

    static void MonitorarComandoDesligar()
    {
        // Exemplo simples de monitorar input na consola.
        // Escreva "DLG" e Enter para sair. Você pode adaptar para Ctrl+T conforme necessário.
        while (!encerrarExecucao)
        {
            var comando = Console.ReadLine();
            if (comando != null && comando.Trim().Equals("DLG", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[AGREGADOR] Encerrando execução... Aguardando resposta do Servidor...");

                // Exemplo de "Desliga" ao Servidor
                try
                {
                    using var server = new TcpClient("127.0.0.1", 11000);
                    using var stream = server.GetStream();
                    var msg = Encoding.UTF8.GetBytes("Desliga");
                    stream.Write(msg, 0, msg.Length);

                    var ackBuffer = new byte[1024];
                    var ackReceived = stream.Read(ackBuffer, 0, ackBuffer.Length);
                    var ack = Encoding.UTF8.GetString(ackBuffer, 0, ackReceived);
                    Console.WriteLine($"[AGREGADOR] Resposta do Servidor: {ack}");
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