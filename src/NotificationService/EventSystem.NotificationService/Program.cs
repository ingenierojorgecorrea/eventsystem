using StackExchange.Redis;
using EventSystem.NotificationService.Services;
using EventSystem.NotificationService.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// ---------------------------------------------------------------
// Redis: IConnectionMultiplexer como Singleton.
// ConnectionMultiplexer mantiene un pool de conexiones al servidor
// Redis y maneja reconexiones automáticamente.
// ---------------------------------------------------------------
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connStr = config["Redis:ConnectionString"] ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(connStr);
});

// RedisNotificationService como Scoped (se crea por request o por scope)
builder.Services.AddScoped<INotificationService, RedisNotificationService>();

// ---------------------------------------------------------------
// LambdaInvokerService: invoca la función AWS Lambda
// "eventsystem-order-processor" después de guardar en Redis.
// Credenciales: variables de entorno o perfil ~/.aws/credentials
// ---------------------------------------------------------------
builder.Services.AddScoped<ILambdaInvokerService, LambdaInvokerService>();

// ---------------------------------------------------------------
// AzureFunctionInvokerService: invoca la Azure Function via HTTP.
// A diferencia de Lambda, no necesita SDK — usa HttpClient estándar.
// ---------------------------------------------------------------
builder.Services.AddHttpClient("AzureFunction");
builder.Services.AddScoped<IAzureFunctionInvokerService, AzureFunctionInvokerService>();

// ---------------------------------------------------------------
// RabbitMqConsumerWorker: BackgroundService que escucha la cola
// de RabbitMQ → Redis → AWS Lambda → Azure Function.
// ---------------------------------------------------------------
builder.Services.AddHostedService<RabbitMqConsumerWorker>();

var app = builder.Build();

app.MapControllers();

app.MapGet("/", () => "NotificationService running on .NET 9");

app.Run();
