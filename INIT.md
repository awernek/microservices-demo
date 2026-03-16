# Como inicializar o projeto

## Pré-requisitos

- [Docker Desktop](https://www.docker.com/products/docker-desktop) instalado e rodando
- [.NET 8 SDK](https://dotnet.microsoft.com/download) (só necessário para desenvolvimento local)

## 1. Clonar o repositório

```bash
git clone <url-do-repo>
cd microservices-demo
```

## 2. Subir os containers

```bash
docker-compose up --build
```

Aguarde até ver no terminal:
```
notification-service  | NotificationService aguardando mensagens...
```

## 3. Verificar o RabbitMQ

Acesse **http://localhost:15672**
- Usuário: `guest`
- Senha: `guest`

## 4. Enviar um pedido de teste

**PowerShell:**
```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5000/orders" `
  -ContentType "application/json" `
  -Body '{"customerName": "João Silva", "totalAmount": 150.90}'
```

**curl (cmd):**
```cmd
curl.exe -X POST http://localhost:5000/orders -H "Content-Type: application/json" -d "{\"customerName\": \"Joao Silva\", \"totalAmount\": 150.90}"
```

## 5. Confirmar que funcionou

No terminal do Docker, o `notification-service` deve exibir:
```
Notificação enviada! Pedido <id> do cliente 'João Silva' no valor de R$ 150,90
```

## Parar os containers

```bash
docker-compose down
```
