using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using EventSystem.Shared.Events;
using EventSystem.Shared.Messaging;
using EventSystem.NotificationService.Services;

namespace EventSystem.NotificationService.Workers;

// RabbitMqConsumerWorker — BackgroundService que escucha la cola RabbitMQ.
//
// FLUJO COMPLETO:
//   1. Recibe OrderCreatedEvent desde RabbitMQ
//   2. Guarda notificación en Redis          (INotificationService)
//   3. Invoca AWS Lambda                     (ILambdaInvokerService)
//   4. Invoca Azure Function                 (IAzureFunctionInvokerService)
//   5. Envía BasicAck a RabbitMQ
//
// Los pasos 2, 3 y 4 son independientes: si Lambda o Azure fallan,
// el ACK se envía igual porque Redis (paso crítico) ya completó.
public sealed class RabbitMqConsumerWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration   _config;
    private readonly ILogger<RabbitMqConsumerWorker> _logger;

    private IConnection? _connection;
    private IChannel?    _channel;

    public RabbitMqConsumerWorker(
        IServiceProvider services,
        IConfiguration   config,
        ILogger<RabbitMqConsumerWorker> logger)
    {
        _services = services;
        _config   = config;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WaitForRabbitMqAsync(stoppingToken);

        _logger.LogInformation("RabbitMQ Consumer started");

        var consumer = new AsyncEventingBasicConsumer(_channel!);

        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var json   = Encoding.UTF8.GetString(ea.Body.Span);
                var @event = JsonSerializer.Deserialize<OrderCreatedEvent>(json);

                if (@event is null)
                {
                    await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                    return;
                }

                _logger.LogInformation("Event received: Order {OrderId} from {Customer}",
                    @event.OrderId, @event.CustomerName);

                using var scope = _services.CreateScope();

                // ── Paso 1: Redis ─────────────────────────────────────────────
                var notificationService = scope.ServiceProvider
                    .GetRequiredService<INotificationService>();

                await notificationService.SaveAsync(@event);
                _logger.LogInformation("Order {OrderId} saved to Redis", @event.OrderId);

                // ── Paso 2: AWS Lambda ────────────────────────────────────────
                var lambdaInvoker = scope.ServiceProvider
                    .GetRequiredService<ILambdaInvokerService>();

                var receipt = await lambdaInvoker.InvokeOrderProcessorAsync(@event);
                if (receipt is not null)
                    _logger.LogInformation(
                        "Lambda → Order {OrderId} | Discount: {Pct}% | Final: ${Final:N2}\n{Receipt}",
                        receipt.OrderId, receipt.DiscountPct, receipt.FinalTotal, receipt.Receipt);

                // ── Paso 3: Azure Function ────────────────────────────────────
                var azureInvoker = scope.ServiceProvider
                    .GetRequiredService<IAzureFunctionInvokerService>();

                var validation = await azureInvoker.ValidateOrderAsync(@event);
                if (validation is not null)
                {
                    var statusIcon  = validation.IsValid ? "✅" : "❌";
                    var errorsBlock = validation.Errors.Length > 0
                        ? string.Join("\n", validation.Errors.Select(e => $"  • {e}"))
                        : "  • ninguno";

                    var report = $"""
                        ============================
                        REPORTE DE VALIDACIÓN
                        ============================
                        Orden   : {validation.OrderId}
                        Estado  : {statusIcon} {validation.Status}
                        ----------------------------
                        Errores :
                        {errorsBlock}
                        ----------------------------
                        Validado: {validation.ValidatedAt:yyyy-MM-dd HH:mm:ss} UTC
                        ============================
                        """;

                    _logger.LogInformation("Azure Function → {Report}", report);
                }

                // ── ACK: todos los pasos completados ──────────────────────────
                await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing RabbitMQ message");
                await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        await _channel!.BasicConsumeAsync(
            queue:    RabbitMqConstants.Queue,
            autoAck:  false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
    }

    private async Task WaitForRabbitMqAsync(CancellationToken ct)
    {
        var hostName = _config["RabbitMQ:Host"] ?? "localhost";
        var factory  = new ConnectionFactory { HostName = hostName };

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _connection = await factory.CreateConnectionAsync(ct);
                _channel    = await _connection.CreateChannelAsync(cancellationToken: ct);

                await _channel.ExchangeDeclareAsync(
                    exchange: RabbitMqConstants.Exchange,
                    type: ExchangeType.Direct,
                    durable: true,
                    cancellationToken: ct);

                await _channel.QueueDeclareAsync(
                    queue:      RabbitMqConstants.Queue,
                    durable:    true,
                    exclusive:  false,
                    autoDelete: false,
                    cancellationToken: ct);

                await _channel.QueueBindAsync(
                    queue:      RabbitMqConstants.Queue,
                    exchange:   RabbitMqConstants.Exchange,
                    routingKey: RabbitMqConstants.RoutingKey,
                    cancellationToken: ct);

                await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, ct);

                _logger.LogInformation("Connected to RabbitMQ at {Host}", hostName);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("RabbitMQ not ready ({Msg}). Retrying in 3s...", ex.Message);
                await Task.Delay(3000, ct);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null) await _channel.CloseAsync();
        if (_connection is not null) await _connection.CloseAsync();
        await base.StopAsync(cancellationToken);
    }
}
