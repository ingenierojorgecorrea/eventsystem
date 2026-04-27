# EventSystem — Microservicios Event-Driven con .NET 9

Sistema de microservicios que integra **RabbitMQ**, **Redis**, **AWS Lambda** y **Azure Functions**
en un flujo event-driven completo. Construido en .NET 9 / .NET 8.

---

## Arquitectura completa

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│                               CLIENTE HTTP                                       │
│              POST /api/orders                    GET /api/notifications          │
└────────────────────┬─────────────────────────────────────────┬───────────────────┘
                     │                                         │
                     ▼                                         ▼
     ┌───────────────────────────┐             ┌───────────────────────────────┐
     │       OrderService        │             │      NotificationService      │
     │         :5001             │             │           :5002               │
     │                           │             │                               │
     │  OrdersController         │             │  NotificationsController      │
     │  POST /api/orders         │             │  GET /api/notifications       │
     │         │                 │             │         │                     │
     │  RabbitMqPublisher        │             │  RedisNotificationService     │
     │  (publica evento)         │             │  (lee de Redis)               │
     └───────────────────────────┘             └───────────────────────────────┘
                     │                                         ▲
                     │ publica OrderCreatedEvent               │ lee
                     ▼                                         │
     ┌───────────────────────────┐                            │
     │         RABBITMQ          │                            │
     │  Exchange: orders.exchange│                            │
     │  Queue: orders.created    │                            │
     │  Routing: order.created   │                            │
     └───────────────┬───────────┘                            │
                     │ consume (BackgroundService)             │
                     ▼                                         │
     ┌───────────────────────────────────────────────────────────────────────────┐
     │                      RabbitMqConsumerWorker                               │
     │                      (NotificationService — BackgroundService)            │
     │                                                                           │
     │   Paso 1 ──▶  RedisNotificationService.SaveAsync()  ──▶  REDIS           │
     │                                                                           │
     │   Paso 2 ──▶  LambdaInvokerService.InvokeOrderProcessorAsync()           │
     │                    │                                                      │
     │                    ▼                                                      │
     │             ┌─────────────────────────────────┐                          │
     │             │       AWS LAMBDA                 │                          │
     │             │  eventsystem-order-processor     │                          │
     │             │  (us-east-2 / .NET 8)            │                          │
     │             │  · Calcula descuento por tramos  │                          │
     │             │  · Genera recibo formateado      │                          │
     │             │  · Retorna OrderResponse         │                          │
     │             └─────────────────────────────────┘                          │
     │                                                                           │
     │   Paso 3 ──▶  AzureFunctionInvokerService.ValidateOrderAsync()           │
     │                    │                                                      │
     │                    ▼                                                      │
     │             ┌─────────────────────────────────┐                          │
     │             │       AZURE FUNCTION             │                          │
     │             │  eventsystem-order-validator     │                          │
     │             │  (HttpTrigger / .NET 8)          │                          │
     │             │  · Valida campos de la orden     │                          │
     │             │  · Retorna APPROVED / REJECTED   │                          │
     │             │  · Lista de errores de validación│                          │
     │             └─────────────────────────────────┘                          │
     └───────────────────────────────────────────────────────────────────────────┘
```

---

## Flujo completo paso a paso

```
1.  Cliente HTTP
      └──▶ POST /api/orders  {customerName, product, total}

2.  OrdersController  (OrderService)
      └──▶ Crea objeto Order en memoria
      └──▶ Construye OrderCreatedEvent (contrato compartido con Shared)
      └──▶ RabbitMqPublisher.PublishAsync(OrderCreatedEvent)

3.  RabbitMQ
      └──▶ Exchange "orders.exchange" recibe el mensaje
      └──▶ Binding enruta por RoutingKey "order.created" → Queue "orders.created.queue"
      └──▶ Mensaje persiste en disco (DeliveryMode.Persistent)

4.  RabbitMqConsumerWorker  (NotificationService — BackgroundService)
      └──▶ AsyncEventingBasicConsumer recibe el mensaje
      └──▶ Deserializa JSON → OrderCreatedEvent

      ── Paso 1: Redis ──────────────────────────────────────────────────
      └──▶ RedisNotificationService.SaveAsync(event)
              └──▶ StringSetAsync  "notifications:{orderId}"  (JSON + TTL 24h)
              └──▶ SetAddAsync     "notifications:index"      (índice de IDs)

      ── Paso 2: AWS Lambda ─────────────────────────────────────────────
      └──▶ LambdaInvokerService.InvokeOrderProcessorAsync(event)
              └──▶ AmazonLambdaClient.InvokeAsync(InvokeRequest)
                      └──▶ Lambda calcula descuento (5% / 10% / 15%)
                      └──▶ Lambda genera recibo formateado
                      └──▶ Retorna OrderResponse con descuento y recibo
              └──▶ Log en consola: RECIBO DE ORDEN con totales y descuento

      ── Paso 3: Azure Function ─────────────────────────────────────────
      └──▶ AzureFunctionInvokerService.ValidateOrderAsync(event)
              └──▶ HttpClient.PostAsync(FunctionUrl + ?code=KEY)
                      └──▶ Azure Function valida campos de la orden
                      └──▶ Retorna IsValid, Status, Errors[]
              └──▶ Log en consola: REPORTE DE VALIDACIÓN con estado y errores

      └──▶ BasicAckAsync → mensaje eliminado de la cola RabbitMQ

5.  Cliente HTTP
      └──▶ GET /api/notifications
              └──▶ NotificationsController
              └──▶ RedisNotificationService.GetAllAsync()
                      └──▶ SMEMBERS "notifications:index"
                      └──▶ GET "notifications:{id}" por cada ID
              └──▶ Retorna lista de notificaciones
```

---

## Estructura del proyecto y archivos clave

```
EventSystem/
├── docker-compose.yml
├── EventSystem.slnx
│
└── src/
    │
    ├── Shared/                                         ← Contratos compartidos entre servicios
    │   └── EventSystem.Shared/
    │       ├── Events/
    │       │   └── OrderCreatedEvent.cs               ← Record inmutable publicado por OrderService
    │       │                                             y consumido por NotificationService
    │       └── Messaging/
    │           └── RabbitMqConstants.cs               ← Exchange, Queue y RoutingKey como constantes
    │                                                     (evita magic strings en ambos servicios)
    │
    ├── OrderService/
    │   └── EventSystem.OrderService/
    │       ├── Controllers/
    │       │   └── OrdersController.cs                ← POST /api/orders
    │       │                                             Crea la orden, construye el evento
    │       │                                             y delega la publicación al publisher
    │       ├── Models/
    │       │   └── Order.cs                           ← Entidad Order + DTO CreateOrderRequest
    │       ├── Services/
    │       │   └── RabbitMqPublisher.cs               ← Implementa IEventPublisher
    │       │                                             Declara Exchange, Queue y Binding
    │       │                                             Serializa evento a JSON y publica
    │       │                                             Registrado como Singleton
    │       └── appsettings.json                       ← Host RabbitMQ + puerto 5001
    │
    ├── NotificationService/
    │   └── EventSystem.NotificationService/
    │       ├── Controllers/
    │       │   └── NotificationsController.cs         ← GET /api/notifications
    │       │                                             Lee directamente de Redis
    │       ├── Services/
    │       │   ├── RedisNotificationService.cs        ← Implementa INotificationService
    │       │   │                                        Escribe y lee notificaciones en Redis
    │       │   │                                        Usa String + Set para indexar por ID
    │       │   │                                        Registrado como Scoped
    │       │   ├── LambdaInvokerService.cs            ← Implementa ILambdaInvokerService
    │       │   │                                        Usa AmazonLambdaClient (AWSSDK.Lambda)
    │       │   │                                        InvocationType.RequestResponse (espera respuesta)
    │       │   │                                        Lee credenciales AWS con FallbackCredentials
    │       │   │                                        Registrado como Scoped
    │       │   └── AzureFunctionInvokerService.cs     ← Implementa IAzureFunctionInvokerService
    │       │                                             Usa IHttpClientFactory para invocar
    │       │                                             el endpoint HTTP de la Azure Function
    │       │                                             La Function Key va en la URL (?code=)
    │       │                                             Registrado como Scoped
    │       ├── Workers/
    │       │   └── RabbitMqConsumerWorker.cs          ← BackgroundService principal
    │       │                                             Orquesta los 3 pasos del flujo:
    │       │                                             Redis → AWS Lambda → Azure Function
    │       │                                             Maneja ACK / NACK con RabbitMQ
    │       │                                             Reintenta conexión si RabbitMQ no está listo
    │       ├── appsettings.json                       ← Host RabbitMQ, Redis, AWS region,
    │       │                                             Azure FunctionUrl (placeholder)
    │       └── appsettings.Local.json                 ← Azure FunctionUrl con key real
    │                                                     (ignorado por .gitignore — solo local)
    │
    ├── Lambda/
    │   └── EventSystem.OrderProcessor/               ← Proyecto AWS Lambda (.NET 8)
    │       ├── Function.cs                           ← Handler: FunctionHandler(OrderRequest)
    │       │                                           Calcula descuento por tramos:
    │       │                                             >= $1000 → 15%
    │       │                                             >= $500  → 10%
    │       │                                             >= $100  → 5%
    │       │                                           Genera recibo formateado (raw string)
    │       ├── Models/
    │       │   ├── OrderRequest.cs                   ← Input de la Lambda (viene de NotificationService)
    │       │   └── OrderResponse.cs                  ← Output: descuento, total final, recibo
    │       └── aws-lambda-tools-defaults.json        ← Config de deploy: región us-east-2,
    │                                                   runtime dotnet8, memoria 256MB
    │
    └── AzureFunction/
        └── EventSystem.OrderValidator/              ← Proyecto Azure Functions v4 (.NET 8)
            ├── ValidateOrderFunction.cs             ← HttpTrigger POST /api/ValidateOrder
            │                                          AuthorizationLevel.Function (requiere ?code=)
            │                                          Valida: nombre, producto, total > 0,
            │                                          total <= 1M, fecha no futura
            │                                          Retorna APPROVED / REJECTED con errores
            ├── Models/
            │   ├── OrderValidationRequest.cs        ← Input (viene de NotificationService)
            │   └── OrderValidationResponse.cs       ← Output: IsValid, Status, Errors[], ValidatedAt
            ├── host.json                            ← Configuración del runtime de Azure Functions
            └── local.settings.json                  ← Config local (FUNCTIONS_WORKER_RUNTIME)
```

---

## Detalle de cada integración

### RabbitMQ — Publisher / Consumer

**Archivo publisher:** `src/OrderService/.../Services/RabbitMqPublisher.cs`
**Archivo consumer:** `src/NotificationService/.../Workers/RabbitMqConsumerWorker.cs`

```
Publisher (OrderService)                     Consumer (NotificationService)
        │                                              │
        │── ExchangeDeclare ──▶ orders.exchange        │
        │── QueueDeclare    ──▶ orders.created.queue   │
        │── QueueBind       ──▶ exchange → queue       │
        │                                              │
        │── BasicPublish ──▶ JSON(OrderCreatedEvent)   │
        │   RoutingKey = "order.created"               │
        │   DeliveryMode = Persistent                  │
        │                       ▼                      │
        │               [orders.created.queue]         │
        │                       │                      │
        │                       └──────────────────────▶ BasicConsume
        │                                              │  autoAck = false
        │                                              │  prefetchCount = 1
        │                                              │
        │                                       procesa mensaje
        │                                              │
        │                                       BasicAck  ← éxito
        │                                       BasicNack ← error (requeue=true)
```

### Redis — Escritura y lectura

**Archivo:** `src/NotificationService/.../Services/RedisNotificationService.cs`

```
ESCRITURA (SaveAsync — llamada desde RabbitMqConsumerWorker):
  StringSetAsync  →  KEY: "notifications:{orderId}"
                     VAL: JSON de Notification
                     TTL: 24 horas

  SetAddAsync     →  KEY: "notifications:index"
                     VAL: orderId  (Set de todos los IDs)

LECTURA (GetAllAsync — llamada desde NotificationsController):
  SetMembersAsync →  SMEMBERS "notifications:index"  → [id1, id2, ...]
  StringGetAsync  →  GET "notifications:{id}"        → JSON por cada ID
```

Verificar desde CLI de Redis:
```bash
docker exec -it redis redis-cli
SMEMBERS notifications:index
GET notifications:{id}
TTL notifications:{id}
```

### AWS Lambda — Cálculo de descuento y recibo

**Archivo invoker:** `src/NotificationService/.../Services/LambdaInvokerService.cs`
**Archivo función:** `src/Lambda/EventSystem.OrderProcessor/Function.cs`

```
NotificationService                          AWS Lambda (us-east-2)
        │                                         │
        │── AmazonLambdaClient.InvokeAsync ──────▶│
        │   FunctionName: eventsystem-order-       │
        │                 processor                │
        │   InvocationType: RequestResponse        │  FunctionHandler(OrderRequest)
        │   Payload: JSON(OrderRequest)            │  ├─ CalculateDiscount(total)
        │                                          │  │    >= $1000 → 15%
        │                                          │  │    >= $500  → 10%
        │                                          │  │    >= $100  → 5%
        │                                          │  │    < $100  → 0%
        │                                          │  └─ BuildReceipt(...)
        │◀── JSON(OrderResponse) ─────────────────│
        │    DiscountPct, FinalTotal, Receipt      │
        │                                          │
        └── Log: RECIBO DE ORDEN en consola
```

Credenciales AWS: se leen automáticamente desde `~/.aws/credentials`
(configurado con `aws configure`).

Desplegar la Lambda:
```bash
cd src/Lambda/EventSystem.OrderProcessor
dotnet lambda deploy-function
```

### Azure Function — Validación de orden

**Archivo invoker:** `src/NotificationService/.../Services/AzureFunctionInvokerService.cs`
**Archivo función:** `src/AzureFunction/EventSystem.OrderValidator/ValidateOrderFunction.cs`

```
NotificationService                          Azure Function (eastus2)
        │                                         │
        │── HttpClient.PostAsync ────────────────▶│
        │   URL: .../api/ValidateOrder            │
        │       ?code={FunctionKey}               │  [HttpTrigger] POST ValidateOrder
        │   Body: JSON(OrderValidationRequest)    │  ├─ CustomerName no vacío
        │                                         │  ├─ CustomerName <= 100 chars
        │                                         │  ├─ Product no vacío
        │                                         │  ├─ Total > 0
        │                                         │  ├─ Total <= 1,000,000
        │                                         │  └─ CreatedAt no es futuro
        │◀── JSON(OrderValidationResponse) ───────│
        │    IsValid, Status, Errors[]            │
        │                                         │
        └── Log: REPORTE DE VALIDACIÓN en consola
```

La **Function Key** se guarda localmente en `appsettings.Local.json` (nunca en git).

Desplegar la Azure Function:
```bash
cd src/AzureFunction/EventSystem.OrderValidator
func azure functionapp publish eventsystem-order-validator
```

---

## Log del NotificationService al procesar una orden

Al crear una orden, el `RabbitMqConsumerWorker` genera estos logs en secuencia:

```
info  Event received: Order abc-123 from Jorge Correa
info  Order abc-123 saved to Redis
info  Invoking Lambda eventsystem-order-processor for Order abc-123
info  Lambda → Order abc-123 | Discount: 15% | Final: $1020.00
      ============================
      RECIBO DE ORDEN
      ============================
      Orden    : abc-123
      Cliente  : Jorge Correa
      Producto : Laptop
      ----------------------------
      Subtotal : $1200.00
      Descuento: 15% (−$180.00)
      TOTAL    : $1020.00
      ----------------------------
      Procesado: 2026-04-27 18:00:00 UTC
      ============================

info  Invoking Azure Function for Order abc-123
info  Azure Function → 
      ============================
      REPORTE DE VALIDACIÓN
      ============================
      Orden   : abc-123
      Estado  : ✅ APPROVED
      ----------------------------
      Errores :
        • ninguno
      ----------------------------
      Validado: 2026-04-27 18:00:01 UTC
      ============================
```

---

## Cómo ejecutar

### 1. Infraestructura local

```bash
docker-compose up -d
# RabbitMQ UI : http://localhost:15672  (guest / guest)
# Redis       : localhost:6379
```

### 2. OrderService

```bash
cd src/OrderService/EventSystem.OrderService
dotnet run
# http://localhost:5001
```

### 3. NotificationService

```bash
cd src/NotificationService/EventSystem.NotificationService
dotnet run
# http://localhost:5002
```

### 4. Crear una orden

```bash
curl -X POST http://localhost:5001/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customerName":"Jorge Correa","product":"Laptop","total":1299.99}'
```

### 5. Ver notificaciones

```bash
curl http://localhost:5002/api/notifications
```

### 6. Ver datos en Redis

```bash
docker exec -it redis redis-cli
SMEMBERS notifications:index
GET notifications:{pega-el-id-aquí}
```

---

## Paquetes NuGet

| Proyecto                | Paquete                                       | Rol                                           |
|-------------------------|-----------------------------------------------|-----------------------------------------------|
| OrderService            | `RabbitMQ.Client 7.x`                         | Publicar mensajes AMQP                        |
| NotificationService     | `RabbitMQ.Client 7.x`                         | Consumir mensajes AMQP                        |
| NotificationService     | `StackExchange.Redis`                         | Cliente Redis con pool de conexiones          |
| NotificationService     | `AWSSDK.Lambda`                               | Invocar AWS Lambda vía SDK                    |
| Lambda (OrderProcessor) | `Amazon.Lambda.Core`                          | Runtime y contexto de la función Lambda       |
| Lambda (OrderProcessor) | `Amazon.Lambda.Serialization.SystemTextJson`  | Serialización JSON automática en Lambda       |
| AzureFunction           | `Microsoft.Azure.Functions.Worker`            | Runtime Azure Functions isolated process      |
| AzureFunction           | `Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore` | HttpTrigger con ASP.NET Core |

---

## Patrones aplicados

| Patrón                | Archivo clave                     | Descripción                                              |
|-----------------------|-----------------------------------|----------------------------------------------------------|
| Event-Driven          | `OrderCreatedEvent.cs`            | Comunicación entre servicios via eventos inmutables      |
| Publisher/Subscriber  | `RabbitMqPublisher.cs` + Worker   | Desacopla productor y consumidor completamente           |
| Shared Contracts      | `EventSystem.Shared`              | Contrato único evita duplicar modelos entre servicios    |
| Background Service    | `RabbitMqConsumerWorker.cs`       | Consumer siempre activo sin bloquear el servidor HTTP    |
| Cache-Aside           | `RedisNotificationService.cs`     | Escritura y lectura desde caché distribuido              |
| Singleton Connection  | `IConnectionMultiplexer`          | Pool de conexiones Redis eficiente                       |
| Scoped per Message    | `IServiceProvider.CreateScope()`  | Servicios Scoped creados por mensaje procesado           |
| Graceful Degradation  | `LambdaInvokerService.cs`         | Si Lambda o Azure fallan, el flujo principal no se rompe |
| Retry on Connect      | `WaitForRabbitMqAsync()`          | Reintento automático si RabbitMQ no está listo al inicio |
