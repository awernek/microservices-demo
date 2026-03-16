# microservices-demo

Projeto de aprendizado de microserviços em C# com RabbitMQ.

## Arquitetura

```
POST /orders
     │
     ▼
┌─────────────────┐        ┌─────────────┐        ┌──────────────────────┐
│   OrderService  │──────▶ │   RabbitMQ  │──────▶ │ NotificationService  │
│  (Web API :5000)│        │  fila:orders│        │   (Worker Service)   │
└─────────────────┘        └─────────────┘        └──────────────────────┘
```

- **OrderService** — recebe pedidos via HTTP e publica uma mensagem na fila `orders`
- **RabbitMQ** — broker de mensagens; garante entrega mesmo que o consumidor esteja offline
- **NotificationService** — consome mensagens da fila e simula o envio de uma notificação

## Pré-requisitos

- [Docker](https://www.docker.com/products/docker-desktop) e Docker Compose
- [.NET 8 SDK](https://dotnet.microsoft.com/download) (só para desenvolvimento local)

## Como rodar

```bash
docker-compose up --build
```

Aguarde os três containers subirem. O NotificationService exibirá:
```
NotificationService aguardando mensagens...
```

## Criar um pedido

**PowerShell:**
```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5000/orders" `
  -ContentType "application/json" `
  -Body '{"customerName": "João Silva", "totalAmount": 150.90}'
```

**curl (Windows cmd):**
```cmd
curl.exe -X POST http://localhost:5000/orders -H "Content-Type: application/json" -d "{\"customerName\": \"Joao Silva\", \"totalAmount\": 150.90}"
```

**curl (Linux/macOS/Git Bash):**
```bash
curl -X POST http://localhost:5000/orders \
  -H "Content-Type: application/json" \
  -d '{"customerName": "João Silva", "totalAmount": 150.90}'
```

Resposta esperada:
```json
{
  "orderId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "status": "Pedido recebido e sendo processado"
}
```

No log do NotificationService você verá:
```
Notificação enviada! Pedido <id> do cliente 'João Silva' no valor de R$ 150,90
```

## Painel do RabbitMQ

Acesse **http://localhost:15672**
- Usuário: `guest`
- Senha: `guest`

No painel você consegue ver a fila `orders`, quantas mensagens estão pendentes, taxa de publicação/consumo, etc.

## Estrutura do projeto

```
microservices-demo/
├── docker-compose.yml
├── OrderService/
│   ├── Controllers/
│   │   └── OrdersController.cs     # endpoint POST /orders
│   ├── Messaging/
│   │   └── RabbitMqPublisher.cs    # publica mensagem no RabbitMQ
│   ├── Models/
│   │   └── OrderCreatedMessage.cs  # contrato da mensagem
│   ├── Program.cs
│   └── Dockerfile
└── NotificationService/
    ├── Models/
    │   └── OrderCreatedMessage.cs  # mesmo contrato
    ├── Worker.cs                   # consome a fila em background
    ├── Program.cs
    └── Dockerfile
```

## Conceitos demonstrados

| Conceito | Onde |
|---|---|
| Publish/Subscribe básico | `RabbitMqPublisher.cs` → `Worker.cs` |
| Fila durável (`durable: true`) | Mensagens sobrevivem a restart do RabbitMQ |
| Mensagem persistente (`Persistent = true`) | Mensagens não são perdidas em crash |
| Ack/Nack manual | `Worker.cs` — só confirma após processar com sucesso |
| Retry simples | Nack com `requeue: true` em caso de erro |
| Wait for dependency | `WaitForRabbitMqAsync` — aguarda o RabbitMQ subir |
