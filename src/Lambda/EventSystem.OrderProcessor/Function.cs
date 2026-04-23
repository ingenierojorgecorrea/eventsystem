using Amazon.Lambda.Core;
using EventSystem.OrderProcessor.Models;

// Serializer para convertir JSON ↔ objetos .NET automáticamente
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace EventSystem.OrderProcessor;

// Lambda Function: Order Processor
//
// RESPONSABILIDAD: Recibe una orden, calcula el descuento según el total
// y devuelve un recibo estructurado.
//
// Esta Lambda es invocada por el NotificationService justo después de
// guardar la notificación en Redis (RequestResponse — espera la respuesta).
public class Function
{
    // FunctionHandler es el punto de entrada que AWS Lambda ejecuta.
    // Input  → OrderRequest  (viene del NotificationService vía JSON)
    // Output → OrderResponse (vuelve al NotificationService como JSON)
    public OrderResponse FunctionHandler(OrderRequest request, ILambdaContext context)
    {
        context.Logger.LogInformation(
            $"Processing order {request.OrderId} for {request.CustomerName} — Total: ${request.Total}");

        var discount       = CalculateDiscount(request.Total);
        var discountAmount = request.Total * (discount / 100m);
        var finalTotal     = request.Total - discountAmount;

        var response = new OrderResponse(
            OrderId:        request.OrderId,
            CustomerName:   request.CustomerName,
            Product:        request.Product,
            OriginalTotal:  request.Total,
            DiscountPct:    discount,
            DiscountAmount: discountAmount,
            FinalTotal:     finalTotal,
            Receipt:        BuildReceipt(request, discount, discountAmount, finalTotal),
            ProcessedAt:    DateTime.UtcNow
        );

        context.Logger.LogInformation(
            $"Order {request.OrderId} processed — Discount: {discount}% — Final: ${finalTotal:N2}");

        return response;
    }

    // Regla de negocio simple para aprendizaje:
    // Total >= 1000 → 15% descuento
    // Total >= 500  → 10% descuento
    // Total >= 100  → 5%  descuento
    // Total < 100   → sin descuento
    private static decimal CalculateDiscount(decimal total) => total switch
    {
        >= 1000 => 15m,
        >= 500  => 10m,
        >= 100  => 5m,
        _       => 0m
    };

    private static string BuildReceipt(
        OrderRequest request,
        decimal discountPct,
        decimal discountAmount,
        decimal finalTotal)
    {
        return $"""
            ============================
            RECIBO DE ORDEN
            ============================
            Orden    : {request.OrderId}
            Cliente  : {request.CustomerName}
            Producto : {request.Product}
            ----------------------------
            Subtotal : ${request.Total:N2}
            Descuento: {discountPct}% (−${discountAmount:N2})
            TOTAL    : ${finalTotal:N2}
            ----------------------------
            Procesado: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
            ============================
            """;
    }
}
