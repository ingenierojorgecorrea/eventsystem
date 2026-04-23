using EventSystem.OrderService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// ---------------------------------------------------------------
// Registrar RabbitMqPublisher como Singleton.
// Se crea de forma async al iniciar la app (la conexión AMQP
// es costosa, se reutiliza durante toda la vida del proceso).
// ---------------------------------------------------------------
builder.Services.AddSingleton<IEventPublisher>(sp =>
{
    var config   = sp.GetRequiredService<IConfiguration>();
    var hostName = config["RabbitMQ:Host"] ?? "localhost";
    return RabbitMqPublisher.CreateAsync(hostName).GetAwaiter().GetResult();
});

var app = builder.Build();

app.MapControllers();

app.MapGet("/", () => "OrderService running on .NET 9");

app.Run();
