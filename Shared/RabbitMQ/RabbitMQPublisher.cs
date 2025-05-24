using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using Shared.Models;

namespace Shared.RabbitMQ
{
    public class RabbitMQPublisher : IDisposable
    {
        private readonly IConnection connection;
        private readonly IModel channel;

        public RabbitMQPublisher()
        {
            var factory = new ConnectionFactory
            {
                HostName = RabbitMQConfig.HOSTNAME,
                UserName = RabbitMQConfig.USERNAME,
                Password = RabbitMQConfig.PASSWORD
            };

            connection = factory.CreateConnection();
            channel = connection.CreateModel();
            
            // Declare exchanges
            channel.ExchangeDeclare(RabbitMQConfig.DATA_EXCHANGE, ExchangeType.Direct);
            channel.ExchangeDeclare(RabbitMQConfig.SHUTDOWN_EXCHANGE, ExchangeType.Fanout);
        }

        public void PublishData(AggregatedData data)
        {
            var message = JsonSerializer.Serialize(data);
            var body = Encoding.UTF8.GetBytes(message);
            
            channel.BasicPublish(exchange: RabbitMQConfig.DATA_EXCHANGE,
                               routingKey: "data",
                               basicProperties: null,
                               body: body);
        }

        public void PublishShutdown(string aggregatorId)
        {
            var shutdownMessage = new { AggregatorId = aggregatorId, Timestamp = DateTime.Now.ToString("o") };
            var message = JsonSerializer.Serialize(shutdownMessage);
            var body = Encoding.UTF8.GetBytes(message);
            
            channel.BasicPublish(exchange: RabbitMQConfig.SHUTDOWN_EXCHANGE,
                               routingKey: "",
                               basicProperties: null,
                               body: body);
        }

        public void Dispose()
        {
            channel?.Dispose();
            connection?.Dispose();
        }
    }
}
