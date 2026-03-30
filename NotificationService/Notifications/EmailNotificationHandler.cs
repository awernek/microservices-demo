using NotificationService.Models;

namespace NotificationService.Notifications;

// Taxa de falha: 30% — simula servidor SMTP instável
public class EmailNotificationHandler : INotificationHandler
{
    private readonly ILogger<EmailNotificationHandler> _logger;

    public string Type => "email";

    public EmailNotificationHandler(ILogger<EmailNotificationHandler> logger) => _logger = logger;

    public async Task HandleAsync(NotificationMessage message)
    {
        await Task.Delay(Random.Shared.Next(50, 150)); // simula latência de envio

        if (Random.Shared.NextDouble() < 0.30)
            throw new InvalidOperationException($"Servidor SMTP indisponível para '{message.Recipient}'");

        _logger.LogInformation("[EMAIL] Enviado para {Recipient} | Pedido {OrderId}", message.Recipient, message.OrderId);
    }
}
