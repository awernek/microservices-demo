using NotificationService.Models;

namespace NotificationService.Notifications;

// Taxa de falha: 40% — simula operadora com instabilidade
public class SmsNotificationHandler : INotificationHandler
{
    private readonly ILogger<SmsNotificationHandler> _logger;

    public string Type => "sms";

    public SmsNotificationHandler(ILogger<SmsNotificationHandler> logger) => _logger = logger;

    public async Task HandleAsync(NotificationMessage message)
    {
        await Task.Delay(Random.Shared.Next(100, 300));

        if (Random.Shared.NextDouble() < 0.40)
            throw new InvalidOperationException($"Operadora indisponível para '{message.Recipient}'");

        _logger.LogInformation("[SMS] Enviado para {Recipient} | Pedido {OrderId}", message.Recipient, message.OrderId);
    }
}
