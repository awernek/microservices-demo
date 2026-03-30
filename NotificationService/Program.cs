using NotificationService;
using NotificationService.Notifications;
using NotificationService.Services;

var builder = Host.CreateApplicationBuilder(args);

// Handlers de notificação — cada um com sua lógica e taxa de falha simulada
builder.Services.AddSingleton<INotificationHandler, EmailNotificationHandler>();
builder.Services.AddSingleton<INotificationHandler, PushNotificationHandler>();
builder.Services.AddSingleton<INotificationHandler, SmsNotificationHandler>();

// Serviços de suporte
builder.Services.AddSingleton<IdempotencyService>();
builder.Services.AddSingleton<MetricsService>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
