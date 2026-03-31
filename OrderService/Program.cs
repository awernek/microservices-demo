using OrderService.Messaging;
using OrderService.Outbox;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();

// Lê o host do RabbitMQ da variável de ambiente (definida no docker-compose)
// Se não existir, usa "localhost" para rodar localmente
var rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";

// Outbox Pattern: controller grava no OutboxStore (in-memory, nunca falha),
// OutboxPublisher drena para o RabbitMQ de forma assíncrona em background.
builder.Services.AddSingleton<OutboxStore>();

// Publisher registrado como Singleton — uma única conexão para toda a vida da aplicação
// Usado pelo OutboxPublisher, não mais pelo controller diretamente.
builder.Services.AddSingleton<IRabbitMqPublisher>(_ =>
    RabbitMqPublisher.CreateAsync(rabbitHost).GetAwaiter().GetResult()
);

builder.Services.AddHostedService<OutboxPublisher>();
builder.Services.AddHealthChecks();

var app = builder.Build();
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();
