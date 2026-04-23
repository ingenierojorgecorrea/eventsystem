using System.Text.Json;
using Amazon;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using Amazon.Runtime;
using EventSystem.Shared.Events;

namespace EventSystem.NotificationService.Services;

// Modelo de respuesta que devuelve la Lambda (espejo de OrderResponse en Lambda project)
public record LambdaOrderResponse(
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

public interface ILambdaInvokerService
{
    Task<LambdaOrderResponse?> InvokeOrderProcessorAsync(
        OrderCreatedEvent @event,
        CancellationToken ct = default);
}

// LambdaInvokerService usa AmazonLambdaClient (AWS SDK) para invocar
// la función "eventsystem-order-processor" de forma síncrona
// (InvocationType = RequestResponse) y obtener el recibo calculado.
//
// Credenciales AWS: se leen automáticamente desde:
//   1. Variables de entorno  (AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY)
//   2. Perfil local          (~/.aws/credentials)
//   3. IAM Role              (si corre en EC2/ECS/Lambda)
public sealed class LambdaInvokerService : ILambdaInvokerService
{
    private readonly AmazonLambdaClient _lambdaClient;
    private readonly string _functionName;
    private readonly ILogger<LambdaInvokerService> _logger;

    public LambdaInvokerService(IConfiguration config, ILogger<LambdaInvokerService> logger)
    {
        _logger       = logger;
        _functionName = config["AWS:LambdaFunctionName"] ?? "eventsystem-order-processor";

        var region    = config["AWS:Region"] ?? "us-east-1";

        // FallbackCredentials intenta todas las fuentes automáticamente
        _lambdaClient = new AmazonLambdaClient(
            FallbackCredentialsFactory.GetCredentials(),
            RegionEndpoint.GetBySystemName(region));
    }

    public async Task<LambdaOrderResponse?> InvokeOrderProcessorAsync(
        OrderCreatedEvent @event,
        CancellationToken ct = default)
    {
        try
        {
            // Construir el payload que recibirá la Lambda (OrderRequest)
            var payload = JsonSerializer.Serialize(new
            {
                OrderId      = @event.OrderId,
                CustomerName = @event.CustomerName,
                Product      = @event.Product,
                Total        = @event.Total,
                CreatedAt    = @event.CreatedAt
            });

            var request = new InvokeRequest
            {
                FunctionName   = _functionName,
                InvocationType = InvocationType.RequestResponse, // Espera la respuesta
                Payload        = payload
            };

            _logger.LogInformation("Invoking Lambda {Function} for Order {OrderId}",
                _functionName, @event.OrderId);

            var response = await _lambdaClient.InvokeAsync(request, ct);

            // Verificar si Lambda ejecutó correctamente (sin errores de función)
            if (response.FunctionError is not null)
            {
                _logger.LogWarning("Lambda returned function error: {Error}", response.FunctionError);
                return null;
            }

            // Leer el JSON de respuesta del stream
            using var reader = new StreamReader(response.Payload);
            var responseJson = await reader.ReadToEndAsync(ct);

            _logger.LogInformation("Lambda response for Order {OrderId}: {Response}",
                @event.OrderId, responseJson);

            return JsonSerializer.Deserialize<LambdaOrderResponse>(responseJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            // No se lanza — si Lambda falla, el flujo principal (Redis) ya completó.
            // Lambda es un paso adicional, no crítico.
            _logger.LogError(ex, "Error invoking Lambda for Order {OrderId}", @event.OrderId);
            return null;
        }
    }
}
