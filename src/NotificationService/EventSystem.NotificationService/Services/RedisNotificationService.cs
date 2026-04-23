using System.Text.Json;
using StackExchange.Redis;
using EventSystem.Shared.Events;

namespace EventSystem.NotificationService.Services;

// Modelo interno que se guarda en Redis
public record Notification(
    Guid    OrderId,
    string  Message,
    DateTime ReceivedAt
);

public interface INotificationService
{
    Task SaveAsync(OrderCreatedEvent @event, CancellationToken ct = default);
    Task<IEnumerable<Notification>> GetAllAsync(CancellationToken ct = default);
}

// RedisNotificationService usa Redis como almacén de notificaciones:
//
//   Estructura en Redis:
//     KEY  → "notifications:{orderId}"  (tipo: String/JSON)
//     SET  → "notifications:index"      (tipo: Set — índice de todos los IDs)
//
//   Esto permite guardar cada notificación individualmente y
//   recuperar todas sin hacer un SCAN completo.
public sealed class RedisNotificationService : INotificationService
{
    private readonly IDatabase _db;
    private const string IndexKey = "notifications:index";

    public RedisNotificationService(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    public async Task SaveAsync(OrderCreatedEvent @event, CancellationToken ct = default)
    {
        var notification = new Notification(
            OrderId:    @event.OrderId,
            Message:    $"Nueva orden de {@event.CustomerName}: {@event.Product} por ${@event.Total:N2}",
            ReceivedAt: DateTime.UtcNow);

        var key  = $"notifications:{@event.OrderId}";
        var json = JsonSerializer.Serialize(notification);

        // Guardar el JSON de la notificación con TTL de 24 horas
        await _db.StringSetAsync(key, json, expiry: TimeSpan.FromHours(24));

        // Agregar el ID al Set de índice para poder recuperar todas
        await _db.SetAddAsync(IndexKey, @event.OrderId.ToString());
    }

    public async Task<IEnumerable<Notification>> GetAllAsync(CancellationToken ct = default)
    {
        // Obtener todos los IDs del índice
        var ids = await _db.SetMembersAsync(IndexKey);

        var tasks = ids.Select(async id =>
        {
            var json = await _db.StringGetAsync($"notifications:{id}");
            if (!json.HasValue) return null;
            return JsonSerializer.Deserialize<Notification>(json!);
        });

        var results = await Task.WhenAll(tasks);
        return results.Where(n => n is not null)!;
    }
}
