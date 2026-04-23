using Microsoft.AspNetCore.Mvc;
using EventSystem.OrderService.Models;
using EventSystem.OrderService.Services;
using EventSystem.Shared.Events;

namespace EventSystem.OrderService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    // Almacenamiento en memoria (para mantener el ejemplo simple).
    // En producción: base de datos (SQL Server, PostgreSQL, etc.).
    private static readonly List<Order> _orders = [];

    private readonly IEventPublisher _publisher;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(IEventPublisher publisher, ILogger<OrdersController> logger)
    {
        _publisher = publisher;
        _logger    = logger;
    }

    // GET api/orders → lista todas las órdenes creadas
    [HttpGet]
    public IActionResult GetAll() => Ok(_orders);

    // POST api/orders → crea una orden y publica el evento a RabbitMQ
    //
    // FLUJO:
    //   1. Recibe CreateOrderRequest del cliente HTTP
    //   2. Crea el objeto Order y lo guarda en memoria
    //   3. Construye OrderCreatedEvent (contrato compartido con NotificationService)
    //   4. Publica el evento al Exchange de RabbitMQ (ASYNC, no bloquea la respuesta)
    //   5. Retorna 201 Created con la orden
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateOrderRequest request,
        CancellationToken ct)
    {
        var order = new Order
        {
            CustomerName = request.CustomerName,
            Product      = request.Product,
            Total        = request.Total
        };

        _orders.Add(order);

        var @event = new OrderCreatedEvent(
            OrderId:      order.Id,
            CustomerName: order.CustomerName,
            Product:      order.Product,
            Total:        order.Total,
            CreatedAt:    order.CreatedAt);

        await _publisher.PublishAsync(@event, ct);

        _logger.LogInformation("Order {OrderId} created and event published", order.Id);

        return CreatedAtAction(nameof(GetAll), new { id = order.Id }, order);
    }
}
