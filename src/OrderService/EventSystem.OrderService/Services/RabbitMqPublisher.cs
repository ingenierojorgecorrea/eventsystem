using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using EventSystem.Shared.Messaging;

namespace EventSystem.OrderService.Services;

// IEventPublisher: abstracción para no acoplar el controller a RabbitMQ directamente.
public interface IEventPublisher
{
    Task PublishAsync<T>(T @event, CancellationToken ct = default);
}

// RabbitMqPublisher implementa el patrón Publisher:
//   1. Declara el Exchange (tipo "direct") si no existe.
//   2. Declara la Queue y la enlaza al Exchange con el RoutingKey.
//   3. Serializa el evento a JSON y lo publica.
//
// Se registra como Singleton porque la conexión AMQP es costosa de crear.
public sealed class RabbitMqPublisher : IEventPublisher, IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel    _channel;

    // El constructor es privado — se usa la factory estática para crear de forma async.
    private RabbitMqPublisher(IConnection connection, IChannel channel)
    {
        _connection = connection;
        _channel    = channel;
    }

    public static async Task<RabbitMqPublisher> CreateAsync(string hostName)
    {
        var factory = new ConnectionFactory { HostName = hostName };
        var connection = await factory.CreateConnectionAsync();
        var channel    = await connection.CreateChannelAsync();

        // Exchange tipo "direct": enruta mensajes exactamente al RoutingKey especificado.
        await channel.ExchangeDeclareAsync(
            exchange: RabbitMqConstants.Exchange,
            type: ExchangeType.Direct,
            durable: true);   // durable=true → sobrevive reinicios de RabbitMQ

        // Cola durable para no perder mensajes si RabbitMQ se reinicia.
        await channel.QueueDeclareAsync(
            queue:      RabbitMqConstants.Queue,
            durable:    true,
            exclusive:  false,
            autoDelete: false);

        // Binding: conecta la cola al exchange bajo el RoutingKey.
        await channel.QueueBindAsync(
            queue:      RabbitMqConstants.Queue,
            exchange:   RabbitMqConstants.Exchange,
            routingKey: RabbitMqConstants.RoutingKey);

        return new RabbitMqPublisher(connection, channel);
    }

    public async Task PublishAsync<T>(T @event, CancellationToken ct = default)
    {
        var json  = JsonSerializer.Serialize(@event);
        var body  = Encoding.UTF8.GetBytes(json);

        var props = new BasicProperties
        {
            ContentType  = "application/json",
            DeliveryMode = DeliveryModes.Persistent,  // Mensaje persistente en disco
        };

        await _channel.BasicPublishAsync(
            exchange:   RabbitMqConstants.Exchange,
            routingKey: RabbitMqConstants.RoutingKey,
            mandatory:  false,
            basicProperties: props,
            body:       body,
            cancellationToken: ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.CloseAsync();
        await _connection.CloseAsync();
    }
}
