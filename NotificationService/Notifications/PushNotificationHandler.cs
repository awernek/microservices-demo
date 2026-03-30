using NotificationService.Models;

namespace NotificationService.Notifications;

// Taxa de falha: 20% — simula dispositivo offline ou token expirado
public class PushNotificationHandler : INotificationHandler
{
    private readonly ILogger<PushNotificationHandler> _logger;

    public string Type => "push";

    public PushNotificationHandler(ILogger<PushNotificationHandler> logger) => _logger = logger;

    public async Task HandleAsync(NotificationMessage message)
    {
        await Task.Delay(Random.Shared.Next(20, 80));

        if (Random.Shared.NextDouble() < 0.20)
            throw new InvalidOperationException($"Token de dispositivo expirado para '{message.Recipient}'");

        _logger.LogInformation("[PUSH] Enviado para {Recipient} | Pedido {OrderId}", message.Recipient, message.OrderId);
    }
}
