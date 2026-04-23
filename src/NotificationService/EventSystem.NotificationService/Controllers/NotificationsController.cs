using Microsoft.AspNetCore.Mvc;
using EventSystem.NotificationService.Services;

namespace EventSystem.NotificationService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications)
    {
        _notifications = notifications;
    }

    // GET api/notifications → devuelve todas las notificaciones guardadas en Redis
    //
    // Cada notificación fue creada por el RabbitMqConsumerWorker al recibir
    // un OrderCreatedEvent del OrderService.
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var notifications = await _notifications.GetAllAsync(ct);
        return Ok(notifications);
    }
}
