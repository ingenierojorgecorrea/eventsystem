namespace EventSystem.Shared.Messaging;

// Constantes compartidas para evitar "magic strings" en ambos servicios.
// Exchange: punto de entrada de los mensajes en RabbitMQ.
// Queue   : cola donde los consumidores escuchan.
public static class RabbitMqConstants
{
    public const string Exchange = "orders.exchange";
    public const string Queue    = "orders.created.queue";
    public const string RoutingKey = "order.created";
}
