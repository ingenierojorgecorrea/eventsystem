namespace EventSystem.OrderValidator.Models;

// Modelo de entrada que recibe la Azure Function desde el NotificationService
public record OrderValidationRequest(
    Guid    OrderId,
    string  CustomerName,
    string  Product,
    decimal Total,
    DateTime CreatedAt
);
