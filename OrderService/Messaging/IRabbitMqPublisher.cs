namespace OrderService.Messaging;

public interface IRabbitMqPublisher
{
    Task PublishAsync<T>(T message);
}
