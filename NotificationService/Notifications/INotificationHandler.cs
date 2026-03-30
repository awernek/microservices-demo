using NotificationService.Models;

namespace NotificationService.Notifications;

public interface INotificationHandler
{
    string Type { get; }
    Task HandleAsync(NotificationMessage message);
}
