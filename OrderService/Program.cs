using OrderService.Messaging;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();

// Lê o host do RabbitMQ da variável de ambiente (definida no docker-compose)
// Se não existir, usa "localhost" para rodar localmente
var rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";

// Registra o publisher como Singleton — uma única conexão para toda a vida da aplicação
builder.Services.AddSingleton(_ =>
    RabbitMqPublisher.CreateAsync(rabbitHost).GetAwaiter().GetResult()
);

var app = builder.Build();
app.MapControllers();
app.Run();
