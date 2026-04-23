namespace EventSystem.Shared.Events;

// Contrato compartido entre microservicios.
// OrderService lo publica → NotificationService lo consume.
// Al ser un record inmutable, es perfecto para mensajes de eventos.
public record OrderCreatedEvent(
    Guid    OrderId,
    string  CustomerName,
    string  Product,
    decimal Total,
    DateTime CreatedAt
);
