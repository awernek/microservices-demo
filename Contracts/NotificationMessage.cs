namespace Contracts;

// Contrato compartilhado entre NotificationService (produtor) e Email/Push/SmsService (consumidores).
// Record permite criar cópias imutáveis com "with" — essencial para o mecanismo de retry:
//   message with { RetryCount = message.RetryCount + 1 }
public record NotificationMessage
{
    public Guid NotificationId { get; init; } = Guid.NewGuid();
    public Guid OrderId { get; init; }
    public string Type { get; init; } = string.Empty;      // "email" | "push" | "sms"
    public string Recipient { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public int RetryCount { get; init; } = 0;
}
