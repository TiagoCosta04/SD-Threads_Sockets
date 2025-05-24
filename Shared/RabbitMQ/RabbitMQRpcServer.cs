using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using Shared.Models;

namespace Shared.RabbitMQ
{
    public class RabbitMQRpcServer : IDisposable
    {
        private readonly IConnection connection;
        private readonly IModel channel;
        private readonly string queueName;
        private readonly Func<RpcRequest, Task<RpcResponse>> requestHandler;

        public RabbitMQRpcServer(string queueName, Func<RpcRequest, Task<RpcResponse>> requestHandler)
        {
            this.queueName = queueName;
            this.requestHandler = requestHandler;

            var factory = new ConnectionFactory
            {
                HostName = RabbitMQConfig.HOSTNAME,
                UserName = RabbitMQConfig.USERNAME,
                Password = RabbitMQConfig.PASSWORD
            };

            connection = factory.CreateConnection();
            channel = connection.CreateModel();
            channel.QueueDeclare(queue: queueName, durable: false, exclusive: false, autoDelete: false);
            channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
        }

        public void Start()
        {
            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += async (model, ea) =>
            {
                string response = "";
                var props = ea.BasicProperties;
                var replyProps = channel.CreateBasicProperties();
                replyProps.CorrelationId = props.CorrelationId;

                try
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    var request = JsonSerializer.Deserialize<RpcRequest>(message);
                    
                    var result = await requestHandler(request ?? new RpcRequest());
                    response = JsonSerializer.Serialize(result);
                }
                catch (Exception ex)
                {
                    var errorResponse = new RpcResponse { Status = "ERROR", Message = ex.Message };
                    response = JsonSerializer.Serialize(errorResponse);
                }
                finally
                {
                    var responseBytes = Encoding.UTF8.GetBytes(response);
                    channel.BasicPublish(exchange: "", routingKey: props.ReplyTo,
                        basicProperties: replyProps, body: responseBytes);
                    channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                }
            };

            channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
        }

        public void Dispose()
        {
            channel?.Dispose();
            connection?.Dispose();
        }
    }
}
