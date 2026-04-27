using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using EventSystem.OrderValidator.Models;

namespace EventSystem.OrderValidator;

// Azure Function con HttpTrigger:
//   - Se activa con un HTTP POST a /api/ValidateOrder
//   - Recibe OrderValidationRequest como JSON en el body
//   - Valida los datos de la orden
//   - Devuelve OrderValidationResponse con el resultado
//
// A diferencia de AWS Lambda (que usa su propio runtime),
// Azure Functions expone un endpoint HTTP estándar que se
// invoca con cualquier HttpClient — no requiere SDK especial.
public class ValidateOrderFunction
{
    private readonly ILogger<ValidateOrderFunction> _logger;

    public ValidateOrderFunction(ILogger<ValidateOrderFunction> logger)
    {
        _logger = logger;
    }

    [Function("ValidateOrder")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "ValidateOrder")] HttpRequestData req)
    {
        _logger.LogInformation("Azure Function ValidateOrder triggered");

        // Deserializar el body JSON → OrderValidationRequest
        var body    = await req.ReadAsStringAsync();
        var request = JsonSerializer.Deserialize<OrderValidationRequest>(body ?? string.Empty,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (request is null)
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("Invalid request body");
            return badRequest;
        }

        _logger.LogInformation("Validating Order {OrderId} for {Customer}",
            request.OrderId, request.CustomerName);

        // ── Reglas de validación ──────────────────────────────────────
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.CustomerName))
            errors.Add("CustomerName is required");

        if (request.CustomerName?.Length > 100)
            errors.Add("CustomerName must not exceed 100 characters");

        if (string.IsNullOrWhiteSpace(request.Product))
            errors.Add("Product is required");

        if (request.Total <= 0)
            errors.Add("Total must be greater than zero");

        if (request.Total > 1_000_000)
            errors.Add("Total exceeds maximum allowed value (1,000,000)");

        if (request.CreatedAt > DateTime.UtcNow.AddMinutes(5))
            errors.Add("CreatedAt cannot be a future date");
        // ─────────────────────────────────────────────────────────────

        var isValid = errors.Count == 0;
        var status  = isValid ? "APPROVED" : "REJECTED";

        _logger.LogInformation("Order {OrderId} validation result: {Status}",
            request.OrderId, status);

        var validationResponse = new OrderValidationResponse(
            OrderId:     request.OrderId,
            IsValid:     isValid,
            Status:      status,
            Errors:      errors.ToArray(),
            ValidatedAt: DateTime.UtcNow
        );

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(JsonSerializer.Serialize(validationResponse));

        return response;
    }
}
