using System.Text;
using System.Text.Json;
using EventSystem.Shared.Events;

namespace EventSystem.NotificationService.Services;

// Modelo espejo de OrderValidationResponse de la Azure Function
public record AzureValidationResponse(
    Guid     OrderId,
    bool     IsValid,
    string   Status,
    string[] Errors,
    DateTime ValidatedAt
);

public interface IAzureFunctionInvokerService
{
    Task<AzureValidationResponse?> ValidateOrderAsync(
        OrderCreatedEvent @event,
        CancellationToken ct = default);
}

// AzureFunctionInvokerService invoca la Azure Function via HTTP POST.
//
// DIFERENCIA CLAVE vs AWS Lambda:
//   - AWS Lambda : requiere AWSSDK.Lambda + AmazonLambdaClient (SDK propietario)
//   - Azure Func : es un endpoint HTTP estándar → basta con HttpClient
//
// URL local  : http://localhost:7071/api/ValidateOrder
// URL en Azure: https://<app>.azurewebsites.net/api/ValidateOrder?code=<key>
//
// El "code" es la Function Key que Azure genera para proteger el endpoint
// (AuthorizationLevel.Function en el atributo [HttpTrigger]).
public sealed class AzureFunctionInvokerService : IAzureFunctionInvokerService
{
    private readonly HttpClient _httpClient;
    private readonly string     _functionUrl;
    private readonly ILogger<AzureFunctionInvokerService> _logger;

    public AzureFunctionInvokerService(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<AzureFunctionInvokerService> logger)
    {
        _httpClient  = httpClientFactory.CreateClient("AzureFunction");
        _functionUrl = config["Azure:FunctionUrl"]
                       ?? "http://localhost:7071/api/ValidateOrder";
        _logger      = logger;
    }

    public async Task<AzureValidationResponse?> ValidateOrderAsync(
        OrderCreatedEvent @event,
        CancellationToken ct = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                OrderId      = @event.OrderId,
                CustomerName = @event.CustomerName,
                Product      = @event.Product,
                Total        = @event.Total,
                CreatedAt    = @event.CreatedAt
            });

            var content  = new StringContent(payload, Encoding.UTF8, "application/json");

            _logger.LogInformation("Invoking Azure Function for Order {OrderId}", @event.OrderId);

            var response = await _httpClient.PostAsync(_functionUrl, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Azure Function returned {Status} for Order {OrderId}",
                    response.StatusCode, @event.OrderId);
                return null;
            }

            var json   = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<AzureValidationResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            _logger.LogInformation(
                "Azure Function validated Order {OrderId} → {Status} | Errors: {Errors}",
                @event.OrderId,
                result?.Status,
                result?.Errors.Length > 0 ? string.Join(", ", result.Errors) : "none");

            return result;
        }
        catch (Exception ex)
        {
            // No es crítico — si Azure Function falla, el flujo principal continúa
            _logger.LogError(ex, "Error invoking Azure Function for Order {OrderId}", @event.OrderId);
            return null;
        }
    }
}
