namespace Shared.RabbitMQ
{
    public static class RabbitMQConfig
    {
        public const string HOSTNAME = "localhost";
        public const string USERNAME = "guest";
        public const string PASSWORD = "guest";
        public const string RPC_QUEUE_PREFIX = "rpc_queue_";
        public const string DATA_EXCHANGE = "data_exchange";
        public const string SHUTDOWN_EXCHANGE = "shutdown_exchange";
    }
}
