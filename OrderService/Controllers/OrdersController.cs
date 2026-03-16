using Microsoft.AspNetCore.Mvc;
using OrderService.Messaging;
using OrderService.Models;

namespace OrderService.Controllers;

[ApiController]
[Route("[controller]")]
public class OrdersController : ControllerBase
{
    private readonly RabbitMqPublisher _publisher;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(RabbitMqPublisher publisher, ILogger<OrdersController> logger)
    {
        _publisher = publisher;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var message = new OrderCreatedMessage
        {
            OrderId = Guid.NewGuid(),
            CustomerName = request.CustomerName,
            TotalAmount = request.TotalAmount,
            CreatedAt = DateTime.UtcNow
        };

        await _publisher.PublishAsync(message);

        _logger.LogInformation("Pedido {OrderId} publicado no RabbitMQ", message.OrderId);

        return Accepted(new { message.OrderId, Status = "Pedido recebido e sendo processado" });
    }
}

public record CreateOrderRequest(string CustomerName, decimal TotalAmount);
