using Microsoft.AspNetCore.Mvc;
using Contracts;
using OrderService.Outbox;

namespace OrderService.Controllers;

[ApiController]
[Route("[controller]")]
public class OrdersController : ControllerBase
{
    private readonly OutboxStore _outbox;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(OutboxStore outbox, ILogger<OrdersController> logger)
    {
        _outbox = outbox;
        _logger = logger;
    }

    [HttpPost]
    public IActionResult CreateOrder([FromBody] CreateOrderRequest request)
    {
        var message = new OrderCreatedMessage
        {
            OrderId      = Guid.NewGuid(),
            CustomerName = request.CustomerName,
            TotalAmount  = request.TotalAmount,
            CreatedAt    = DateTime.UtcNow
        };

        // Grava no Outbox — operação local, nunca falha por indisponibilidade do RabbitMQ.
        // O OutboxPublisher drena para o RabbitMQ de forma assíncrona em background.
        _outbox.Enqueue(message);

        _logger.LogInformation("Pedido {OrderId} gravado no Outbox", message.OrderId);

        return Accepted(new { message.OrderId, Status = "Pedido recebido e sendo processado" });
    }
}

public record CreateOrderRequest(string CustomerName, decimal TotalAmount);
