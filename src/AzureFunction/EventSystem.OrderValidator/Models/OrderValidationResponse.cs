namespace EventSystem.OrderValidator.Models;

// Modelo de respuesta que devuelve la Azure Function
public record OrderValidationResponse(
    Guid     OrderId,
    bool     IsValid,
    string   Status,
    string[] Errors,
    DateTime ValidatedAt
);
