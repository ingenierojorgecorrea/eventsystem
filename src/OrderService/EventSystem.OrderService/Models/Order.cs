namespace EventSystem.OrderService.Models;

public class Order
{
    public Guid    Id           { get; init; } = Guid.NewGuid();
    public string  CustomerName { get; set; } = string.Empty;
    public string  Product      { get; set; } = string.Empty;
    public decimal Total        { get; set; }
    public DateTime CreatedAt   { get; init; } = DateTime.UtcNow;
}

// DTO para recibir la solicitud del cliente via POST
public class CreateOrderRequest
{
    public string  CustomerName { get; set; } = string.Empty;
    public string  Product      { get; set; } = string.Empty;
    public decimal Total        { get; set; }
}
