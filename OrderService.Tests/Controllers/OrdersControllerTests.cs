using Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using OrderService.Controllers;
using OrderService.Outbox;

namespace OrderService.Tests.Controllers;

public class OrdersControllerTests
{
    private readonly OutboxStore _outbox = new();
    private readonly OrdersController _controller;

    public OrdersControllerTests()
    {
        _controller = new OrdersController(
            _outbox,
            NullLogger<OrdersController>.Instance
        );
    }

    [Fact]
    public void CreateOrder_DeveRetornar202_QuandoRequestValido()
    {
        var request = new CreateOrderRequest("João Silva", 150.90m);

        var result = _controller.CreateOrder(request);

        Assert.IsType<AcceptedResult>(result);
    }

    [Fact]
    public void CreateOrder_DeveEnfileirarMensagemNoOutbox()
    {
        var request = new CreateOrderRequest("João Silva", 150.90m);

        _controller.CreateOrder(request);

        Assert.Equal(1, _outbox.Count);
    }

    [Fact]
    public void CreateOrder_MensagemEnfileirada_DeveTerDadosCorretos()
    {
        var request = new CreateOrderRequest("Maria Costa", 299.99m);

        _controller.CreateOrder(request);

        _outbox.TryPeek(out var mensagem);
        Assert.NotNull(mensagem);
        Assert.Equal("Maria Costa", mensagem!.CustomerName);
        Assert.Equal(299.99m, mensagem.TotalAmount);
        Assert.NotEqual(Guid.Empty, mensagem.OrderId);
    }

    [Fact]
    public void CreateOrder_ResponseBody_DeveConterOrderId()
    {
        var request = new CreateOrderRequest("Carlos Lima", 50m);

        var result = _controller.CreateOrder(request) as AcceptedResult;

        var body    = result!.Value!;
        var orderId = body.GetType().GetProperty("OrderId")?.GetValue(body);
        var status  = body.GetType().GetProperty("Status")?.GetValue(body) as string;

        Assert.NotNull(orderId);
        Assert.Equal("Pedido recebido e sendo processado", status);
    }
}
