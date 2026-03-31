using PushService;
using PushService.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<IdempotencyService>();
builder.Services.AddSingleton<MetricsService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
