namespace NotificationService.Models;

// Record permite usar "with" para criar cópias imutáveis (usado no retry)
public record NotificationMessage
{
    public Guid NotificationId { get; init; } = Guid.NewGuid();
    public Guid OrderId { get; init; }
    public string Type { get; init; } = string.Empty;      // "email" | "push" | "sms"
    public string Recipient { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public int RetryCount { get; init; } = 0;
}
