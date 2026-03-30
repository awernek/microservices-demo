# microservices-demo

Projeto de aprendizado de microservicos em C# com RabbitMQ.

Para subir o ambiente rapidamente, veja [INIT.md](INIT.md).

## O que mudou (upgrades)

- Pipeline agora em duas etapas no `NotificationService`: `orders` -> `notifications`.
- Cada pedido gera 3 notificacoes (`email`, `push`, `sms`).
- Retry com backoff exponencial (5s, 15s, 45s).
- Dead Letter Queue para mensagens que estouram o limite de tentativas.
- Idempotencia para evitar reprocessamento de notificacoes duplicadas.
- Metricas periodicas de processamento, falhas, duplicatas e DLQ.

## Arquitetura

```text
POST /orders
     |
     v
+-----------------+
|   OrderService  |
|  (Web API :5000)|
+--------+--------+
         | publica em `orders`
         v
+------------------------------------------------------+
|                      RabbitMQ                        |
| orders -> OrderConsumer -> notifications.exchange   |
| notifications <- retry queues <- retry.exchange     |
| notifications.dlq (mensagens esgotadas)             |
+------------------------------------------------------+
         |
         v
+------------------------------------------------------+
|               NotificationService (Worker)           |
| OrderConsumer + NotificationConsumer + handlers      |
+------------------------------------------------------+
```

## Pre-requisitos

- [Docker](https://www.docker.com/products/docker-desktop) e Docker Compose
- [.NET 8 SDK](https://dotnet.microsoft.com/download) (para desenvolvimento local e execucao de testes)

## Como rodar

```bash
docker compose up --build
```

Opcional (Compose v1):

```bash
docker-compose up --build
```

Quando tudo estiver pronto, o `notification-service` deve logar:

```text
NotificationService pronto. Aguardando mensagens...
```

## Criar um pedido

**PowerShell**

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5000/orders" `
  -ContentType "application/json" `
  -Body '{"customerName": "Joao Silva", "totalAmount": 150.90}'
```

**curl (Windows cmd)**

```cmd
curl.exe -X POST http://localhost:5000/orders -H "Content-Type: application/json" -d "{\"customerName\": \"Joao Silva\", \"totalAmount\": 150.90}"
```

**curl (Linux/macOS/Git Bash)**

```bash
curl -X POST http://localhost:5000/orders \
  -H "Content-Type: application/json" \
  -d '{"customerName": "Joao Silva", "totalAmount": 150.90}'
```

Resposta esperada:

```json
{
  "orderId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "status": "Pedido recebido e sendo processado"
}
```

## O que observar nos logs

Apos publicar um pedido, voce vera eventos como:

- `OrderConsumer` despachando 3 notificacoes.
- Handlers (`EMAIL`, `PUSH`, `SMS`) processando mensagens.
- Logs de retry: `[RETRY 1/3]`, `[RETRY 2/3]`, etc.
- Eventual envio para DLQ: `[DLQ] ...`.
- Resumo de metricas a cada 30 segundos.

## Topologia RabbitMQ

Exchanges:

- `notifications.exchange` (principal)
- `notifications.retry.exchange` (retry)

Filas:

- `orders`
- `notifications`
- `notifications.retry.1` (TTL 5s)
- `notifications.retry.2` (TTL 15s)
- `notifications.retry.3` (TTL 45s)
- `notifications.dlq`

## Testes

Na raiz do repositorio:

```bash
dotnet test
```

Projetos:

- `OrderService.Tests` (unitarios com xUnit + Moq)
- `Integration.Tests` (integracao com RabbitMQ via Testcontainers)

## Painel RabbitMQ

- URL: <http://localhost:15672>
- Usuario: `guest`
- Senha: `guest`

No painel voce pode inspecionar filas, mensagens prontas, taxa de consumo e conteudo de DLQ.

## Estrutura do projeto

```text
microservices-demo/
|- docker-compose.yml
|- microservices-demo.sln
|- OrderService/
|  |- Controllers/OrdersController.cs
|  |- Messaging/
|  |  |- IRabbitMqPublisher.cs
|  |  |- RabbitMqPublisher.cs
|  |- Models/OrderCreatedMessage.cs
|  |- Program.cs
|  |- Dockerfile
|- NotificationService/
|  |- Messaging/
|  |  |- RabbitMqTopology.cs
|  |  |- OrderConsumer.cs
|  |  |- NotificationConsumer.cs
|  |- Models/
|  |  |- OrderCreatedMessage.cs
|  |  |- NotificationMessage.cs
|  |- Notifications/
|  |  |- INotificationHandler.cs
|  |  |- EmailNotificationHandler.cs
|  |  |- PushNotificationHandler.cs
|  |  |- SmsNotificationHandler.cs
|  |- Services/
|  |  |- IdempotencyService.cs
|  |  |- MetricsService.cs
|  |- Worker.cs
|  |- Program.cs
|  |- Dockerfile
|- OrderService.Tests/
|  |- Controllers/OrdersControllerTests.cs
|- Integration.Tests/
   |- OrderCreatedFlowTests.cs
```
