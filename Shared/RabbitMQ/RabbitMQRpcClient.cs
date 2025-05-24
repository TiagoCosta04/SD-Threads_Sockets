using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Shared.Models;

namespace Shared.RabbitMQ
{
    public class RabbitMQRpcClient : IDisposable
    {
        private readonly IConnection connection;
        private readonly IModel channel;
        private readonly string replyQueueName;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<RpcResponse>> callbackMapper = new();

        public RabbitMQRpcClient()
        {
            var factory = new ConnectionFactory
            {
                HostName = RabbitMQConfig.HOSTNAME,
                UserName = RabbitMQConfig.USERNAME,
                Password = RabbitMQConfig.PASSWORD
            };

            connection = factory.CreateConnection();
            channel = connection.CreateModel();
            replyQueueName = channel.QueueDeclare().QueueName;

            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += (model, ea) =>
            {
                if (!callbackMapper.TryRemove(ea.BasicProperties.CorrelationId, out var tcs))
                    return;

                var body = ea.Body.ToArray();
                var response = JsonSerializer.Deserialize<RpcResponse>(Encoding.UTF8.GetString(body));
                tcs.TrySetResult(response ?? new RpcResponse { Status = "ERROR", Message = "Invalid response" });
            };

            channel.BasicConsume(consumer: consumer, queue: replyQueueName, autoAck: true);
        }

        public async Task<RpcResponse> CallAsync(string queueName, RpcRequest request, TimeSpan timeout)
        {
            var correlationId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<RpcResponse>();
            callbackMapper[correlationId] = tcs;

            var props = channel.CreateBasicProperties();
            props.CorrelationId = correlationId;
            props.ReplyTo = replyQueueName;

            var messageBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));
            channel.BasicPublish(exchange: "", routingKey: queueName, basicProperties: props, body: messageBytes);

            using var cts = new CancellationTokenSource(timeout);
            cts.Token.Register(() => 
            {
                callbackMapper.TryRemove(correlationId, out _);
                tcs.TrySetCanceled();
            });

            return await tcs.Task;
        }

        public void Dispose()
        {
            channel?.Dispose();
            connection?.Dispose();
        }
    }
}
