# EventSystem — Microservicios con RabbitMQ + Redis en .NET 9

## Arquitectura

```
┌─────────────────────────────────────────────────────────────────┐
│                        CLIENTE HTTP                             │
│                  POST /api/orders  →  GET /api/notifications    │
└──────────────┬──────────────────────────────────┬──────────────┘
               │                                  │
               ▼                                  ▼
┌──────────────────────────┐        ┌──────────────────────────────┐
│      OrderService        │        │     NotificationService      │
│      :5001               │        │     :5002                    │
│                          │        │                              │
│  OrdersController        │        │  NotificationsController     │
│  ┌──────────────────┐    │        │  ┌──────────────────────┐   │
│  │ POST /api/orders │    │        │  │ GET /api/notifications│   │
│  └────────┬─────────┘    │        │  └──────────┬───────────┘   │
│           │              │        │             │               │
│  RabbitMqPublisher       │        │  RedisNotificationService    │
│  (Singleton)             │        │  (Scoped)                   │
└──────────────────────────┘        └──────────────────────────────┘
               │                                  ▲
               │  PUBLICA                         │ GUARDA
               │  OrderCreatedEvent               │ Notification
               ▼                                  │
┌──────────────────────────┐        ┌─────────────┴──────────────┐
│         RABBITMQ          │        │          REDIS              │
│                           │        │                            │
│  Exchange: orders.exchange│──────▶ │  BackgroundService         │
│  Queue: orders.created    │ CONSUME│  RabbitMqConsumerWorker    │
│  RoutingKey: order.created│        │                            │
└───────────────────────────┘        └────────────────────────────┘
```

## Flujo completo paso a paso

```
1. Cliente HTTP  →  POST /api/orders  →  OrdersController
2. OrdersController  →  Crea Order en memoria
3. OrdersController  →  RabbitMqPublisher.PublishAsync(OrderCreatedEvent)
4. RabbitMQ recibe el mensaje en la Queue "orders.created.queue"
5. RabbitMqConsumerWorker (NotificationService) recibe el mensaje
6. RabbitMqConsumerWorker  →  RedisNotificationService.SaveAsync()
7. Redis almacena la notificación con TTL 24h
8. Cliente HTTP  →  GET /api/notifications  →  Lee de Redis
```

## Estructura del proyecto

```
EventSystem/
├── docker-compose.yml                          ← RabbitMQ + Redis
├── EventSystem.sln
└── src/
    ├── Shared/
    │   └── EventSystem.Shared/
    │       ├── Events/
    │       │   └── OrderCreatedEvent.cs        ← Contrato compartido (record)
    │       └── Messaging/
    │           └── RabbitMqConstants.cs        ← Exchange, Queue, RoutingKey
    │
    ├── OrderService/
    │   └── EventSystem.OrderService/
    │       ├── Controllers/
    │       │   └── OrdersController.cs         ← POST /api/orders
    │       ├── Models/
    │       │   └── Order.cs
    │       ├── Services/
    │       │   └── RabbitMqPublisher.cs        ← Publica a RabbitMQ
    │       └── appsettings.json                ← Puerto 5001
    │
    └── NotificationService/
        └── EventSystem.NotificationService/
            ├── Controllers/
            │   └── NotificationsController.cs  ← GET /api/notifications
            ├── Services/
            │   └── RedisNotificationService.cs ← Lee/escribe en Redis
            ├── Workers/
            │   └── RabbitMqConsumerWorker.cs   ← BackgroundService
            └── appsettings.json                ← Puerto 5002
```

## Conceptos clave

### RabbitMQ — Exchange, Queue, Binding

```
Publisher                 RabbitMQ                     Consumer
   │                         │                            │
   │── BasicPublish ─────────▶ Exchange (orders.exchange) │
   │   RoutingKey=            │                            │
   │   "order.created"        │── Binding ──────────────▶ Queue
   │                         │   (RoutingKey match)       │ (orders.created.queue)
   │                         │                            │── BasicConsume ──▶ Worker
```

- **Exchange**: Punto de entrada. Recibe mensajes y los enruta.
- **Queue**: Cola donde esperan los mensajes hasta ser consumidos.
- **Binding**: Regla que conecta Exchange → Queue por RoutingKey.
- **ACK/NACK**: El consumidor confirma (ACK) o rechaza (NACK) cada mensaje.

### Redis — Estructura de datos

```
Redis Keys:
  "notifications:{orderId}"  →  STRING (JSON de la Notification) + TTL 24h
  "notifications:index"      →  SET (todos los OrderIds)

Para recuperar todas las notificaciones:
  1. SMEMBERS notifications:index       → [id1, id2, id3, ...]
  2. GET notifications:{id1}            → JSON
  3. GET notifications:{id2}            → JSON
  ...
```

### BackgroundService (IHostedService)

El `RabbitMqConsumerWorker` hereda de `BackgroundService`. ASP.NET Core lo
inicia automáticamente al arrancar la app y lo detiene cuando la app termina.
Corre en segundo plano sin bloquear el servidor HTTP.

## Cómo ejecutar

### 1. Levantar la infraestructura

```bash
docker-compose up -d
```

Verifica que están corriendo:
- RabbitMQ Management UI: http://localhost:15672 (guest/guest)
- Redis: localhost:6379

### 2. Ejecutar OrderService (terminal 1)

```bash
cd src/OrderService/EventSystem.OrderService
dotnet run
# Corre en http://localhost:5001
```

### 3. Ejecutar NotificationService (terminal 2)

```bash
cd src/NotificationService/EventSystem.NotificationService
dotnet run
# Corre en http://localhost:5002
```

### 4. Crear una orden

```bash
curl -X POST http://localhost:5001/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customerName":"Jorge","product":"Laptop","total":1299.99}'
```

### 5. Ver notificaciones en Redis

```bash
curl http://localhost:5002/api/notifications
```

Deberías ver la notificación generada automáticamente por el evento.

### 6. Ver el evento en RabbitMQ Management UI

Accede a http://localhost:15672 → Queues → `orders.created.queue`
Puedes ver métricas de mensajes entrantes/procesados en tiempo real.

## Paquetes NuGet utilizados

| Proyecto              | Paquete               | Rol                              |
|-----------------------|-----------------------|----------------------------------|
| OrderService          | RabbitMQ.Client 7.x   | Publicar mensajes AMQP           |
| NotificationService   | RabbitMQ.Client 7.x   | Consumir mensajes AMQP           |
| NotificationService   | StackExchange.Redis   | Cliente Redis (pool de conexiones)|

## Patrones aplicados

| Patrón                  | Dónde                        | Por qué                              |
|-------------------------|------------------------------|--------------------------------------|
| Event-Driven            | RabbitMQ Exchange/Queue      | Desacopla servicios                  |
| Publisher/Subscriber    | RabbitMqPublisher + Worker   | Comunicación asíncrona               |
| Shared Contracts        | EventSystem.Shared           | Evita duplicar modelos de eventos    |
| Background Service      | RabbitMqConsumerWorker       | Consumer siempre activo              |
| Cache-Aside             | RedisNotificationService     | Persistencia rápida de notificaciones|
| Singleton Connection    | IConnectionMultiplexer       | Pool de conexiones Redis eficiente   |
