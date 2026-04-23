namespace EventSystem.OrderProcessor.Models;

// Modelo de respuesta que la Lambda devuelve al NotificationService.
public record OrderResponse(
    Guid    OrderId,
    string  CustomerName,
    string  Product,
    decimal OriginalTotal,
    decimal DiscountPct,
    decimal DiscountAmount,
    decimal FinalTotal,
    string  Receipt,
    DateTime ProcessedAt
);
