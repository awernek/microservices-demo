using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderService.Controllers;
using OrderService.Messaging;
using OrderService.Models;

namespace OrderService.Tests.Controllers;

public class OrdersControllerTests
{
    // Mock: um "dublê" do publisher que não conecta em lugar nenhum
    // Só registra se foi chamado e com quais argumentos
    private readonly Mock<IRabbitMqPublisher> _publisherMock = new();
    private readonly OrdersController _controller;

    public OrdersControllerTests()
    {
        _controller = new OrdersController(
            _publisherMock.Object,
            NullLogger<OrdersController>.Instance  // logger que descarta tudo — não precisamos de logs nos testes
        );
    }

    [Fact]
    public async Task CreateOrder_DeveRetornar202_QuandoRequestValido()
    {
        var request = new CreateOrderRequest("João Silva", 150.90m);

        var result = await _controller.CreateOrder(request);

        // Verifica que retornou HTTP 202 Accepted
        Assert.IsType<AcceptedResult>(result);
    }

    [Fact]
    public async Task CreateOrder_DevePublicarMensagemNoRabbitMQ()
    {
        var request = new CreateOrderRequest("João Silva", 150.90m);

        await _controller.CreateOrder(request);

        // Verifica que PublishAsync foi chamado exatamente 1 vez com um OrderCreatedMessage
        _publisherMock.Verify(
            p => p.PublishAsync(It.IsAny<OrderCreatedMessage>()),
            Times.Once
        );
    }

    [Fact]
    public async Task CreateOrder_MensagemPublicada_DeveTerDadosCorretos()
    {
        var request = new CreateOrderRequest("Maria Costa", 299.99m);
        OrderCreatedMessage? mensagemCapturada = null;

        // Captura o argumento que foi passado para o PublishAsync
        _publisherMock
            .Setup(p => p.PublishAsync(It.IsAny<OrderCreatedMessage>()))
            .Callback<OrderCreatedMessage>(msg => mensagemCapturada = msg);

        await _controller.CreateOrder(request);

        Assert.NotNull(mensagemCapturada);
        Assert.Equal("Maria Costa", mensagemCapturada.CustomerName);
        Assert.Equal(299.99m, mensagemCapturada.TotalAmount);
        Assert.NotEqual(Guid.Empty, mensagemCapturada.OrderId);  // garantia de que um Guid foi gerado
    }

    [Fact]
    public async Task CreateOrder_ResponseBody_DeveConterOrderId()
    {
        var request = new CreateOrderRequest("Carlos Lima", 50m);

        var result = await _controller.CreateOrder(request) as AcceptedResult;

        // Inspeciona o objeto anônimo retornado no body
        var body = result!.Value!;
        var orderId = body.GetType().GetProperty("OrderId")?.GetValue(body);
        var status = body.GetType().GetProperty("Status")?.GetValue(body) as string;

        Assert.NotNull(orderId);
        Assert.Equal("Pedido recebido e sendo processado", status);
    }
}
