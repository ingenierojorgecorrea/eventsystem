namespace EventSystem.OrderProcessor.Models;

// Modelo de entrada que el NotificationService envía a la Lambda.
// Debe ser serializable a JSON (System.Text.Json).
public record OrderRequest(
    Guid    OrderId,
    string  CustomerName,
    string  Product,
    decimal Total,
    DateTime CreatedAt
);
