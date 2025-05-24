using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace Shared.RabbitMQ
{
    public class RabbitMQSubscriber : IDisposable
    {
        private readonly IConnection connection;
        private readonly IModel channel;
        private readonly string queueName;

        public RabbitMQSubscriber(string queueName)
        {
            this.queueName = queueName;
            
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
            
            // Declare queue
            channel.QueueDeclare(queue: queueName, durable: false, exclusive: false, autoDelete: false);
        }

        public void SubscribeToData(Action<string> onDataReceived)
        {
            channel.QueueBind(queue: queueName, exchange: RabbitMQConfig.DATA_EXCHANGE, routingKey: "data");
            
            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                onDataReceived(message);
                channel.BasicAck(ea.DeliveryTag, false);
            };
            
            channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
        }

        public void SubscribeToShutdown(Action<string> onShutdownReceived)
        {
            var shutdownQueue = channel.QueueDeclare().QueueName;
            channel.QueueBind(queue: shutdownQueue, exchange: RabbitMQConfig.SHUTDOWN_EXCHANGE, routingKey: "");
            
            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                onShutdownReceived(message);
                channel.BasicAck(ea.DeliveryTag, false);
            };
            
            channel.BasicConsume(queue: shutdownQueue, autoAck: false, consumer: consumer);
        }

        public void Dispose()
        {
            channel?.Dispose();
            connection?.Dispose();
        }
    }
}
