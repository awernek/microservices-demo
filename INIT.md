# Como inicializar o projeto

Guia rapido para subir e validar o ambiente local. Para detalhes completos, veja [README.md](README.md).

## Pre-requisitos

- [Docker Desktop](https://www.docker.com/products/docker-desktop) instalado e em execucao
- [.NET 8 SDK](https://dotnet.microsoft.com/download) (opcional; necessario para rodar testes locais)

## 1) Clonar o repositorio

```bash
git clone <url-do-repo>
cd microservices-demo
```

## 2) Subir os containers

```bash
docker compose up --build
```

Alternativa (Compose v1):

```bash
docker-compose up --build
```

Espere ate aparecer no log do worker:

```text
NotificationService pronto. Aguardando mensagens...
```

## 3) Validar o RabbitMQ

Abra <http://localhost:15672>:

- Usuario: `guest`
- Senha: `guest`

## 4) Enviar um pedido de teste

**PowerShell**

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5000/orders" `
  -ContentType "application/json" `
  -Body '{"customerName": "Joao Silva", "totalAmount": 150.90}'
```

## 5) Confirmar processamento ponta a ponta

No terminal do `notification-service`, confirme sinais de processamento:

- Pedido recebido pelo `OrderConsumer`
- Despacho de 3 notificacoes (`email`, `push`, `sms`)
- Possiveis retries (`[RETRY x/3]`) e, se necessario, DLQ (`[DLQ]`)
- Metricas resumidas a cada 30 segundos

## 6) Rodar testes (opcional)

```bash
dotnet test
```

## 7) Parar o ambiente

```bash
docker compose down
```

Alternativa: `docker-compose down`.
