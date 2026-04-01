# Tutorial Completo: Microserviços com C# e RabbitMQ

## Do Zero ao Deploy — Um Guia Passo a Passo

---

## Sumário

1. [Introdução — O que vamos construir?](#1-introdução--o-que-vamos-construir)
2. [Conceitos Fundamentais](#2-conceitos-fundamentais)
3. [Pré-requisitos — O que você precisa instalar](#3-pré-requisitos--o-que-você-precisa-instalar)
4. [Etapa 1 — Criando a Estrutura do Projeto](#4-etapa-1--criando-a-estrutura-do-projeto)
5. [Etapa 2 — Contracts (o Projeto Compartilhado)](#5-etapa-2--contracts-o-projeto-compartilhado)
6. [Etapa 3 — OrderService (a API de Pedidos)](#6-etapa-3--orderservice-a-api-de-pedidos)
7. [Etapa 4 — NotificationService (o Dispatcher)](#7-etapa-4--notificationservice-o-dispatcher)
8. [Etapa 5 — EmailService (Worker de E-mail)](#8-etapa-5--emailservice-worker-de-e-mail)
9. [Etapa 6 — PushService e SmsService](#9-etapa-6--pushservice-e-smsservice)
10. [Etapa 7 — Docker e Docker Compose](#10-etapa-7--docker-e-docker-compose)
11. [Etapa 8 — Testes Unitários](#11-etapa-8--testes-unitários)
12. [Etapa 9 — Testes de Integração](#12-etapa-9--testes-de-integração)
13. [Etapa 10 — Rodando o Projeto Completo](#13-etapa-10--rodando-o-projeto-completo)
14. [Glossário](#14-glossário)
15. [Diagrama Completo da Arquitetura](#15-diagrama-completo-da-arquitetura)
16. [Dicas e Próximos Passos](#16-dicas-e-próximos-passos)

---

## 1. Introdução — O que vamos construir?

Imagine que você tem uma loja online. Quando um cliente faz um pedido, você precisa:

1. **Registrar o pedido** (anotar que ele existe).
2. **Avisar o cliente** por e-mail, SMS e notificação push no celular.

Agora, imagine que o envio de e-mail demora 3 segundos, o SMS às vezes falha, e a notificação push pode não chegar. Se tudo isso rodar junto no mesmo "clique" do usuário, ele vai ficar esperando uma eternidade — e se o SMS falhar, o pedido inteiro pode dar erro.

**A solução?** Separar as responsabilidades em **6 programas independentes** (microserviços) que se comunicam por **mensagens**:

```text
┌─────────────────┐         ┌──────────────┐
│  Seu navegador   │──POST──▶│ OrderService │
│  (ou Postman)    │◀──202───│  (API Web)   │
└─────────────────┘         └──────┬───────┘
                                   │ Outbox → RabbitMQ
                                   ▼
                          ┌──────────────────────┐
                          │ NotificationService   │
                          │ (Dispatcher)          │
                          └───┬──────┬──────┬────┘
                              │      │      │
                    ┌─────────┘      │      └─────────┐
                    ▼                ▼                 ▼
             ┌────────────┐  ┌────────────┐  ┌────────────┐
             │EmailService│  │PushService │  │ SmsService │
             └────────────┘  └────────────┘  └────────────┘
```

- **OrderService**: recebe o pedido via HTTP, grava no **Outbox** (uma fila local) e responde "recebido!" instantaneamente. Um processo em segundo plano drena o Outbox para o RabbitMQ.
- **NotificationService**: ouve a fila `orders`. Para cada pedido, gera 3 notificações e as roteia pelo tipo (email, push, sms) através de uma exchange.
- **EmailService**: ouve apenas notificações do tipo `email`. Processa com retry e DLQ.
- **PushService**: ouve apenas notificações do tipo `push`. Processa com retry e DLQ.
- **SmsService**: ouve apenas notificações do tipo `sms`. Processa com retry e DLQ.

**E o RabbitMQ?** É o "correio" que conecta todos eles. Funciona como uma agência dos correios: recebe mensagens de quem envia e as entrega na caixa certa de quem deve receber.

### Por que isso é útil?

- **O cliente não espera**: a resposta é instantânea.
- **Se uma notificação falhar, o pedido não falha**: são coisas separadas.
- **Cada serviço pode escalar sozinho**: muitos SMSs na fila? Sobe mais instâncias do SmsService. Muitos pedidos? Escala o OrderService.
- **Se o RabbitMQ cair momentaneamente, nada se perde**: o Outbox segura as mensagens até que ele volte.
- **Cada equipe pode cuidar do seu serviço**: o time de e-mail mexe no EmailService sem afetar o SMS.

---

## 2. Conceitos Fundamentais

Antes de colocar a mão no código, vamos entender os conceitos que vamos usar. Pense neles como as peças de um quebra-cabeça — cada uma faz sentido sozinha, mas juntas formam o projeto completo.

### 2.1 Microserviços

Um **microserviço** é um programa pequeno e independente que faz **uma coisa bem feita**. Em vez de um programa gigante que faz tudo (chamado de "monolito"), você divide em pedaços menores.

**Analogia**: pense em uma pizzaria. Em vez de uma única pessoa que faz a massa, coloca o recheio, assa e entrega, você tem especialistas: um pizzaiolo, um forno automático e um entregador. Cada um trabalha no seu ritmo.

No nosso projeto, temos **6 microserviços** — cada um faz exatamente uma coisa:

| Serviço | Tipo | Responsabilidade |
|---------|------|------------------|
| OrderService | API Web | Recebe pedidos via HTTP |
| NotificationService | Worker | Transforma pedidos em 3 notificações e roteia |
| EmailService | Worker | Processa notificações de e-mail |
| PushService | Worker | Processa notificações push |
| SmsService | Worker | Processa notificações SMS |
| Contracts | Biblioteca | Modelos compartilhados entre todos os serviços |

### 2.2 RabbitMQ — O Carteiro das Mensagens

O **RabbitMQ** é um programa que funciona como um correio. Seu OrderService escreve uma "carta" (mensagem) e coloca no correio. Os outros serviços vão lá e pegam a carta quando estiverem prontos.

Conceitos do RabbitMQ que vamos usar:

| Conceito | O que é | Analogia |
|----------|---------|----------|
| **Queue (Fila)** | Onde as mensagens ficam esperando | A caixa de correio |
| **Exchange** | O "roteador" que decide para qual fila a mensagem vai | O carteiro que lê o endereço |
| **Routing Key** | O "endereço" na mensagem | O CEP na carta |
| **Binding** | A regra que liga uma exchange a uma fila | "Cartas com CEP 01310 vão para a caixa do João" |
| **Producer** | Quem envia a mensagem | Quem escreve e envia a carta |
| **Consumer** | Quem recebe e processa a mensagem | Quem abre e lê a carta |
| **Ack (Acknowledge)** | Confirmação de que a mensagem foi processada | "Li a carta, pode descartar" |
| **DLQ (Dead Letter Queue)** | Fila para mensagens que falharam demais | A seção de "cartas perdidas" do correio |

### 2.3 Docker — A Caixa Mágica

O **Docker** empacota seu programa e tudo que ele precisa (sistema operacional, dependências, configurações) em uma "caixa" chamada **container**. Essa caixa roda igual em qualquer computador.

**Analogia**: é como mandar uma mudança numa caixa fechada. Não importa se a pessoa vai colocar no apartamento ou na casa — dentro da caixa, tudo está organizado do mesmo jeito.

- **Dockerfile**: a receita que ensina como montar a caixa.
- **Docker Compose**: o "maestro" que sobe várias caixas de uma vez (RabbitMQ + todos os serviços).

### 2.4 Padrões que vamos usar

| Padrão | O que faz | Onde usamos |
|--------|-----------|-------------|
| **Outbox Pattern** | Grava a mensagem localmente antes de enviar ao broker | OrderService grava no `OutboxStore`, o `OutboxPublisher` envia ao RabbitMQ em background |
| **Factory Method assíncrono** | Cria objetos com lógica assíncrona no construtor | `RabbitMqPublisher.CreateAsync()` |
| **Retry com Backoff Exponencial** | Tenta de novo com esperas cada vez maiores | Filas de retry (5s, 15s, 45s) em cada serviço de notificação |
| **Dead Letter Queue** | Destino para mensagens que falharam demais | `notifications.email.dlq`, `notifications.push.dlq`, `notifications.sms.dlq` |
| **Idempotência** | Garante que processar a mesma mensagem duas vezes não causa problemas | `IdempotencyService` em cada serviço de notificação |
| **Shared Contracts** | Modelos compartilhados entre serviços via projeto comum | Projeto `Contracts` |
| **Injeção de Dependência (DI)** | Os objetos recebem suas dependências em vez de criá-las | Todo o projeto usa DI nativo do .NET |
| **Healthcheck** | Verificação de saúde do serviço | `docker-compose` verifica se o RabbitMQ está pronto antes de subir os serviços |

### 2.5 O Padrão Outbox — Em Detalhes

O **Outbox Pattern** é uma das melhorias mais importantes do projeto. Imagine o cenário sem ele:

```text
1. Controller recebe pedido
2. Controller publica no RabbitMQ ← e se o RabbitMQ estiver fora do ar?
3. Controller retorna 202
```

Se o RabbitMQ estiver indisponível no passo 2, o pedido é **perdido**. O cliente receberia um erro 500.

Com o Outbox Pattern:

```text
1. Controller recebe pedido
2. Controller grava no OutboxStore (fila local em memória) ← nunca falha!
3. Controller retorna 202 imediatamente
4. (em background) OutboxPublisher drena o OutboxStore → RabbitMQ
5. Se falhar, tenta de novo no próximo ciclo (1 segundo)
```

O truque: **o passo 2 é uma operação local que nunca falha**. Mesmo que o RabbitMQ esteja fora do ar, o pedido fica seguro no OutboxStore e será enviado quando o RabbitMQ voltar.

> **Em produção**, o OutboxStore seria uma **tabela no banco de dados** (mesma transação que salva o pedido), garantindo atomicidade total. No nosso demo, usamos `ConcurrentQueue` em memória para simplicidade.

### 2.6 O que é uma API REST?

Uma **API REST** é uma forma de programas se comunicarem pela internet usando HTTP (o mesmo protocolo que seu navegador usa). No nosso caso:

- Seu navegador (ou Postman ou curl) manda uma **requisição HTTP POST** para `http://localhost:5000/orders`
- O OrderService recebe, processa e devolve uma **resposta HTTP 202** ("Aceito, estou cuidando disso")

---

## 3. Pré-requisitos — O que você precisa instalar

Antes de começar, instale estas ferramentas. Siga a ordem.

### 3.1 .NET 8 SDK

O SDK é o kit de desenvolvimento do .NET — ele permite criar, compilar e rodar projetos C#.

1. Acesse: https://dotnet.microsoft.com/download
2. Baixe o **.NET 8 SDK** (não o Runtime — o SDK já inclui o Runtime).
3. Instale normalmente (next, next, finish).
4. Verifique no terminal:

```powershell
dotnet --version
```

Deve mostrar algo como `8.0.xxx`.

### 3.2 Docker Desktop

O Docker vai rodar o RabbitMQ e os nossos serviços em containers.

1. Acesse: https://www.docker.com/products/docker-desktop
2. Baixe e instale o Docker Desktop.
3. Abra o Docker Desktop e espere ele iniciar (o ícone da baleia na bandeja do sistema deve ficar verde).
4. Verifique no terminal:

```powershell
docker --version
docker compose version
```

### 3.3 Um editor de código

Recomendo o **Visual Studio Code** ou o **Visual Studio 2022**. Se estiver usando o **Cursor**, melhor ainda.

### 3.4 Git (opcional, mas recomendado)

Para versionar seu código:

```powershell
git --version
```

Se não tiver, baixe em: https://git-scm.com/downloads

---

## 4. Etapa 1 — Criando a Estrutura do Projeto

Vamos criar tudo do zero, comando por comando. Abra seu terminal (PowerShell) e siga.

### 4.1 Criar a pasta do projeto

```powershell
mkdir microservices-demo
cd microservices-demo
```

### 4.2 Inicializar o Git

```powershell
git init
```

### 4.3 Criar o arquivo .gitignore

Crie um arquivo chamado `.gitignore` na raiz do projeto com o seguinte conteúdo:

```text
# Build
bin/
obj/
out/

# .NET
*.user
*.suo
.vs/

# Logs
*.log

# Docker
.dockerignore
```

**O que é o .gitignore?** Ele diz ao Git quais arquivos e pastas ignorar. As pastas `bin/` e `obj/` são geradas automaticamente na compilação — não faz sentido versioná-las.

### 4.4 Criar a Solution (.sln)

Uma **Solution** é um arquivo que agrupa todos os projetos .NET de uma aplicação. Pense nela como um "guarda-chuva" que contém todos os projetos.

```powershell
dotnet new sln -n microservices-demo
```

### 4.5 Criar todos os projetos

```powershell
dotnet new classlib -n Contracts
dotnet new webapi -n OrderService --no-openapi
dotnet new worker -n NotificationService
dotnet new worker -n EmailService
dotnet new worker -n PushService
dotnet new worker -n SmsService
dotnet new xunit -n OrderService.Tests
dotnet new xunit -n Integration.Tests
```

**O que cada template faz:**

| Comando | Tipo | Para quê |
|---------|------|----------|
| `classlib` | Biblioteca de classes | Projeto `Contracts` — só contém modelos, sem lógica de execução |
| `webapi` | API Web | OrderService — aceita requisições HTTP |
| `worker` | Worker Service | Serviços de background — ficam rodando continuamente |
| `xunit` | Projeto de testes | Testes unitários e de integração |

### 4.6 Adicionar todos os projetos à Solution

```powershell
dotnet sln add Contracts/Contracts.csproj
dotnet sln add OrderService/OrderService.csproj
dotnet sln add NotificationService/NotificationService.csproj
dotnet sln add EmailService/EmailService.csproj
dotnet sln add PushService/PushService.csproj
dotnet sln add SmsService/SmsService.csproj
dotnet sln add OrderService.Tests/OrderService.Tests.csproj
dotnet sln add Integration.Tests/Integration.Tests.csproj
```

### 4.7 Verificar a estrutura

```powershell
dotnet sln list
```

Deve mostrar:

```text
Projeto(s)
----------
Contracts\Contracts.csproj
OrderService\OrderService.csproj
NotificationService\NotificationService.csproj
EmailService\EmailService.csproj
PushService\PushService.csproj
SmsService\SmsService.csproj
OrderService.Tests\OrderService.Tests.csproj
Integration.Tests\Integration.Tests.csproj
```

### 4.8 Estrutura de pastas final

Quando terminarmos, a estrutura completa será:

```text
microservices-demo/
├── .gitignore
├── docker-compose.yml
├── microservices-demo.sln
├── Contracts/
│   ├── Contracts.csproj
│   ├── OrderCreatedMessage.cs
│   └── NotificationMessage.cs
├── OrderService/
│   ├── Controllers/OrdersController.cs
│   ├── Messaging/
│   │   ├── IRabbitMqPublisher.cs
│   │   └── RabbitMqPublisher.cs
│   ├── Outbox/
│   │   ├── OutboxStore.cs
│   │   └── OutboxPublisher.cs
│   ├── Program.cs
│   └── Dockerfile
├── NotificationService/
│   ├── Messaging/
│   │   ├── RabbitMqTopology.cs
│   │   └── OrderConsumer.cs
│   ├── Worker.cs
│   ├── Program.cs
│   └── Dockerfile
├── EmailService/
│   ├── Messaging/
│   │   ├── RabbitMqTopology.cs
│   │   └── NotificationConsumer.cs
│   ├── Services/
│   │   ├── IdempotencyService.cs
│   │   └── MetricsService.cs
│   ├── Worker.cs
│   ├── Program.cs
│   └── Dockerfile
├── PushService/
│   └── (mesma estrutura do EmailService)
├── SmsService/
│   └── (mesma estrutura do EmailService)
├── OrderService.Tests/
│   ├── Controllers/OrdersControllerTests.cs
│   └── Outbox/
│       ├── OutboxStoreTests.cs
│       └── OutboxPublisherTests.cs
└── Integration.Tests/
    └── OrderCreatedFlowTests.cs
```

---

## 5. Etapa 2 — Contracts (o Projeto Compartilhado)

O projeto `Contracts` contém os **modelos de dados** que são compartilhados entre os serviços. Sem ele, cada serviço teria sua própria cópia dessas classes — e se você mudasse um campo, teria que lembrar de mudar em todos os lugares.

### 5.1 O .csproj do Contracts

O arquivo `Contracts/Contracts.csproj` é o mais simples possível:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

Note que o SDK é `Microsoft.NET.Sdk` (sem `.Web` ou `.Worker`) — é uma biblioteca pura, sem infraestrutura de execução.

### 5.2 OrderCreatedMessage

Apague qualquer arquivo `Class1.cs` gerado pelo template e crie `Contracts/OrderCreatedMessage.cs`:

```csharp
namespace Contracts;

public class OrderCreatedMessage
{
    public Guid OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

**Explicando cada propriedade:**
- `OrderId`: um identificador único (GUID) para o pedido. Exemplo: `a1b2c3d4-e5f6-...`
- `CustomerName`: o nome do cliente.
- `TotalAmount`: o valor total do pedido (usamos `decimal` para dinheiro, nunca `float` ou `double`, porque eles perdem precisão com centavos).
- `CreatedAt`: quando o pedido foi criado.

### 5.3 NotificationMessage

Crie `Contracts/NotificationMessage.cs`:

```csharp
namespace Contracts;

public record NotificationMessage
{
    public Guid NotificationId { get; init; } = Guid.NewGuid();
    public Guid OrderId { get; init; }
    public string Type { get; init; } = string.Empty;      // "email" | "push" | "sms"
    public string Recipient { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public int RetryCount { get; init; } = 0;
}
```

**Por que é `record` e não `class`?**

A diferença principal aqui é a palavra-chave `with`. Records permitem criar **cópias imutáveis** com valores alterados:

```csharp
var original = new NotificationMessage { Type = "email", RetryCount = 0 };
var comRetry = original with { RetryCount = 1 };
```

Isso cria um **novo objeto** com `RetryCount = 1` e todos os outros campos iguais ao original. Usamos isso no mecanismo de retry dos serviços de notificação.

**O que é `init`?** A keyword `init` permite que a propriedade seja definida apenas durante a inicialização do objeto. Depois disso, ela se torna somente leitura.

### 5.4 Por que um projeto compartilhado?

Em produção, contratos entre microserviços são frequentemente distribuídos como **pacotes NuGet internos**. Para o nosso demo, usar um `ProjectReference` é mais simples e atinge o mesmo objetivo: **uma única fonte de verdade** para os modelos.

---

## 6. Etapa 3 — OrderService (a API de Pedidos)

O OrderService é o ponto de entrada do sistema. Ele:

1. Recebe um pedido via HTTP POST.
2. Grava a mensagem no **OutboxStore** (operação local, nunca falha).
3. Responde "recebido!" ao cliente instantaneamente.
4. Em background, o **OutboxPublisher** drena o store e envia ao RabbitMQ.

### 6.1 Instalar pacotes e referências

```powershell
cd OrderService
dotnet add package RabbitMQ.Client --version 7.2.1
dotnet add reference ../Contracts/Contracts.csproj
cd ..
```

### 6.2 Verificar o .csproj

O arquivo `OrderService/OrderService.csproj` deve ficar assim:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="RabbitMQ.Client" Version="7.2.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Contracts\Contracts.csproj" />
  </ItemGroup>

</Project>
```

### 6.3 Criar a estrutura de pastas

```powershell
mkdir OrderService/Messaging
mkdir OrderService/Controllers
mkdir OrderService/Outbox
```

### 6.4 Criar a Interface — IRabbitMqPublisher

Crie `OrderService/Messaging/IRabbitMqPublisher.cs`:

```csharp
namespace OrderService.Messaging;

public interface IRabbitMqPublisher
{
    Task PublishAsync<T>(T message);
}
```

Uma interface é como um **contrato**: "qualquer classe que me implemente precisa ter o método `PublishAsync`". Isso nos permite criar mocks para testes e trocar a implementação sem alterar quem usa.

### 6.5 Criar a Implementação — RabbitMqPublisher

Crie `OrderService/Messaging/RabbitMqPublisher.cs`:

```csharp
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace OrderService.Messaging;

public class RabbitMqPublisher : IRabbitMqPublisher, IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private const string QueueName = "orders";

    private RabbitMqPublisher(IConnection connection, IChannel channel)
    {
        _connection = connection;
        _channel = channel;
    }

    public static async Task<RabbitMqPublisher> CreateAsync(string host, int port = 5672)
    {
        var factory = new ConnectionFactory { HostName = host, Port = port };
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();

        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false
        );

        return new RabbitMqPublisher(connection, channel);
    }

    public async Task PublishAsync<T>(T message)
    {
        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);

        var props = new BasicProperties
        {
            Persistent = true
        };

        await _channel.BasicPublishAsync(
            exchange: "",
            routingKey: QueueName,
            mandatory: false,
            basicProperties: props,
            body: body
        );
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
```

**Pontos importantes:**

- **Construtor privado + `CreateAsync`**: conectar ao RabbitMQ é assíncrono, e construtores C# não suportam `async`. Então usamos o padrão Factory Method Assíncrono.
- **`durable: true`**: a fila sobrevive a um restart do RabbitMQ.
- **`Persistent = true`**: a mensagem é salva em disco pelo RabbitMQ.
- **`exchange: ""`**: usa a exchange padrão, que entrega a mensagem diretamente na fila cujo nome é igual à routing key.

### 6.6 Criar o OutboxStore

Crie `OrderService/Outbox/OutboxStore.cs`:

```csharp
using System.Collections.Concurrent;
using Contracts;

namespace OrderService.Outbox;

public class OutboxStore
{
    private readonly ConcurrentQueue<OrderCreatedMessage> _queue = new();

    public void Enqueue(OrderCreatedMessage message) => _queue.Enqueue(message);

    public bool TryPeek(out OrderCreatedMessage? message) => _queue.TryPeek(out message);

    public bool TryDequeue(out OrderCreatedMessage? message) => _queue.TryDequeue(out message);

    public int Count => _queue.Count;
}
```

**O que é ConcurrentQueue?**

É uma fila thread-safe — múltiplos threads podem adicionar e remover itens ao mesmo tempo sem problemas. É perfeita para o Outbox: o controller adiciona, o publisher remove.

**Três operações-chave:**
- `Enqueue`: adiciona no final da fila.
- `TryPeek`: **olha** o primeiro item **sem remover**. Crítico para o Outbox — olhamos antes de publicar.
- `TryDequeue`: remove e retorna o primeiro item. Só chamamos **depois** de confirmar que o publish foi bem-sucedido.

### 6.7 Criar o OutboxPublisher

Crie `OrderService/Outbox/OutboxPublisher.cs`:

```csharp
using OrderService.Messaging;

namespace OrderService.Outbox;

public class OutboxPublisher : BackgroundService
{
    private readonly OutboxStore _store;
    private readonly IRabbitMqPublisher _publisher;
    private readonly ILogger<OutboxPublisher> _logger;

    public OutboxPublisher(
        OutboxStore store,
        IRabbitMqPublisher publisher,
        ILogger<OutboxPublisher> logger)
    {
        _store     = store;
        _publisher = publisher;
        _logger    = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            while (_store.TryPeek(out var message))
            {
                try
                {
                    await _publisher.PublishAsync(message!);
                    _store.TryDequeue(out _);
                    _logger.LogDebug("[Outbox] Mensagem {OrderId} publicada", message!.OrderId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("[Outbox] Falha ao publicar {OrderId}: {Error}. Retry em 1s.",
                        message!.OrderId, ex.Message);
                    break;
                }
            }
        }
    }
}
```

**A lógica crítica do Outbox:**

```text
1. TryPeek  → olha a mensagem (sem remover)
2. Publish  → envia ao RabbitMQ
3. TryDequeue → só agora remove da fila (após confirmação de envio)
```

**Por que TryPeek antes de TryDequeue?** Se fizéssemos `TryDequeue` antes de publicar e o RabbitMQ falhasse, a mensagem seria **perdida** — já foi removida da fila local, mas nunca chegou ao broker. Com `TryPeek`, a mensagem permanece na fila até que o envio seja confirmado.

**PeriodicTimer**: a cada 1 segundo, o publisher verifica se há mensagens no Outbox e tenta enviá-las. Se falhar, o `break` interrompe o dreno e tenta novamente no próximo ciclo.

### 6.8 Criar o Controller — OrdersController

Se houver algum controller de exemplo gerado pelo template, apague-o. Crie `OrderService/Controllers/OrdersController.cs`:

```csharp
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

        _outbox.Enqueue(message);

        _logger.LogInformation("Pedido {OrderId} gravado no Outbox", message.OrderId);

        return Accepted(new { message.OrderId, Status = "Pedido recebido e sendo processado" });
    }
}

public record CreateOrderRequest(string CustomerName, decimal TotalAmount);
```

**Diferença importante em relação ao padrão anterior:** o controller agora é **síncrono** (`IActionResult` em vez de `async Task<IActionResult>`). Ele não precisa mais esperar o RabbitMQ — apenas grava no OutboxStore (operação em memória, instantânea) e retorna.

**Os atributos do controller:**
- `[ApiController]`: ativa validação automática do body. Se o JSON estiver inválido, o ASP.NET retorna 400 automaticamente.
- `[Route("[controller]")]`: a URL base é `/orders` (nome do controller sem o sufixo "Controller").
- `[HttpPost]`: responde a requisições POST.
- `[FromBody]`: converte o JSON do corpo da requisição para `CreateOrderRequest`.
- `Accepted()`: retorna HTTP **202** ("Aceito, estou cuidando disso em background").

### 6.9 Configurar o Program.cs do OrderService

Substitua todo o conteúdo do arquivo `OrderService/Program.cs`:

```csharp
using OrderService.Messaging;
using OrderService.Outbox;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();

var rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";

builder.Services.AddSingleton<OutboxStore>();

builder.Services.AddSingleton<IRabbitMqPublisher>(_ =>
    RabbitMqPublisher.CreateAsync(rabbitHost).GetAwaiter().GetResult()
);

builder.Services.AddHostedService<OutboxPublisher>();
builder.Services.AddHealthChecks();

var app = builder.Build();
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();
```

**Entendendo as novidades:**

1. **`OutboxStore` como Singleton**: uma única instância compartilhada entre o controller (que enfileira) e o OutboxPublisher (que drena).

2. **`OutboxPublisher` como HostedService**: `AddHostedService` registra o OutboxPublisher como um `BackgroundService` que inicia automaticamente com a aplicação.

3. **Health Checks**: `AddHealthChecks()` + `MapHealthChecks("/health")` cria um endpoint `/health` que retorna 200 se o serviço está rodando. Útil para Docker, Kubernetes e monitoramento.

### 6.10 Configurar o appsettings.json

O `OrderService/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

---

## 7. Etapa 4 — NotificationService (o Dispatcher)

O NotificationService foi simplificado para ser um **dispatcher puro**: ele lê pedidos da fila `orders` e gera 3 notificações, roteando cada uma pelo tipo (`email`, `push`, `sms`) para o serviço correto.

Ele **não processa** as notificações — apenas as distribui. A lógica de retry, DLQ, idempotência e métricas agora vive nos serviços dedicados (EmailService, PushService, SmsService).

### 7.1 Instalar pacotes e referências

```powershell
cd NotificationService
dotnet add package RabbitMQ.Client --version 7.2.1
dotnet add package Microsoft.Extensions.Hosting --version 8.0.1
dotnet add reference ../Contracts/Contracts.csproj
cd ..
```

### 7.2 Criar a estrutura de pastas

```powershell
mkdir NotificationService/Messaging
```

### 7.3 Criar a Topologia — RabbitMqTopology

A topologia do NotificationService é agora **mínima** — ele declara apenas a exchange compartilhada e a fila de pedidos.

Crie `NotificationService/Messaging/RabbitMqTopology.cs`:

```csharp
using RabbitMQ.Client;

namespace NotificationService.Messaging;

public static class RabbitMqTopology
{
    public const string NotificationsExchange = "notifications.exchange";

    public static async Task DeclareAsync(IChannel channel)
    {
        await channel.ExchangeDeclareAsync(
            exchange: NotificationsExchange,
            type: ExchangeType.Direct,
            durable: true
        );

        await channel.QueueDeclareAsync(
            queue: "orders",
            durable: true,
            exclusive: false,
            autoDelete: false
        );
    }
}
```

**Exchange Direct**: entrega a mensagem para filas cuja binding corresponde à routing key. Quando publicamos com `routingKey: "email"`, apenas filas que fizeram binding com routing key `"email"` recebem a mensagem.

### 7.4 Criar o OrderConsumer

Crie `NotificationService/Messaging/OrderConsumer.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NotificationService.Messaging;

public class OrderConsumer
{
    private readonly IChannel _consumeChannel;
    private readonly IChannel _publishChannel;
    private readonly ILogger _logger;

    public OrderConsumer(IChannel consumeChannel, IChannel publishChannel, ILogger logger)
    {
        _consumeChannel = consumeChannel;
        _publishChannel = publishChannel;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await _consumeChannel.BasicQosAsync(
            prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: ct
        );

        var consumer = new AsyncEventingBasicConsumer(_consumeChannel);
        consumer.ReceivedAsync += HandleOrderAsync;

        await _consumeChannel.BasicConsumeAsync(
            queue: "orders",
            autoAck: false,
            consumer: consumer,
            cancellationToken: ct
        );

        _logger.LogInformation("[OrderConsumer] Aguardando pedidos...");
    }

    private async Task HandleOrderAsync(object _, BasicDeliverEventArgs args)
    {
        var json = Encoding.UTF8.GetString(args.Body.ToArray());
        var order = JsonSerializer.Deserialize<OrderCreatedMessage>(json)!;

        var notifications = new[]
        {
            new NotificationMessage
            {
                OrderId   = order.OrderId,
                Type      = "email",
                Recipient = $"{order.CustomerName.ToLower().Replace(" ", ".")}@example.com",
                Content   = $"Pedido #{order.OrderId} confirmado. Total: {order.TotalAmount:C}"
            },
            new NotificationMessage
            {
                OrderId   = order.OrderId,
                Type      = "push",
                Recipient = $"device-{order.OrderId:N}"[..16],
                Content   = $"Seu pedido de {order.TotalAmount:C} foi recebido!"
            },
            new NotificationMessage
            {
                OrderId   = order.OrderId,
                Type      = "sms",
                Recipient = "+55 11 9 0000-0000",
                Content   = $"Pedido recebido. Total: R$ {order.TotalAmount:F2}"
            }
        };

        foreach (var notification in notifications)
        {
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(notification));
            var props = new BasicProperties { Persistent = true };

            await _publishChannel.BasicPublishAsync(
                exchange: RabbitMqTopology.NotificationsExchange,
                routingKey: notification.Type,
                mandatory: false,
                basicProperties: props,
                body: body
            );
        }

        _logger.LogInformation(
            "[OrderConsumer] Pedido {OrderId} → {Count} notificações despachadas (email, push, sms)",
            order.OrderId,
            notifications.Length
        );

        await _consumeChannel.BasicAckAsync(args.DeliveryTag, multiple: false);
    }
}
```

**A mudança mais importante em relação à versão anterior:** a routing key agora é **dinâmica** — `routingKey: notification.Type`. Isso significa:

- Notificação do tipo `"email"` → routing key `"email"` → vai para a fila `notifications.email` do EmailService.
- Notificação do tipo `"push"` → routing key `"push"` → vai para a fila `notifications.push` do PushService.
- Notificação do tipo `"sms"` → routing key `"sms"` → vai para a fila `notifications.sms` do SmsService.

Cada serviço faz o binding da sua fila na `notifications.exchange` com a routing key correspondente ao seu tipo.

### 7.5 Criar o Worker

Substitua `NotificationService/Worker.cs`:

```csharp
using NotificationService.Messaging;
using RabbitMQ.Client;

namespace NotificationService;

public class Worker : BackgroundService
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _rabbitHost;

    public Worker(ILoggerFactory loggerFactory, IConfiguration config)
    {
        _loggerFactory = loggerFactory;
        _rabbitHost    = config["RabbitMQ:Host"] ?? "localhost";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var logger = _loggerFactory.CreateLogger<Worker>();

        await WaitForRabbitMqAsync(logger, stoppingToken);

        var factory = new ConnectionFactory { HostName = _rabbitHost };
        await using var connection = await factory.CreateConnectionAsync(stoppingToken);

        await using var setupChannel        = await connection.CreateChannelAsync(
            cancellationToken: stoppingToken);
        await using var orderConsumeChannel = await connection.CreateChannelAsync(
            cancellationToken: stoppingToken);
        await using var orderPublishChannel = await connection.CreateChannelAsync(
            cancellationToken: stoppingToken);

        await RabbitMqTopology.DeclareAsync(setupChannel);
        logger.LogInformation("Topologia RabbitMQ declarada com sucesso.");

        var orderConsumer = new OrderConsumer(
            orderConsumeChannel,
            orderPublishChannel,
            _loggerFactory.CreateLogger<OrderConsumer>()
        );
        await orderConsumer.StartAsync(stoppingToken);

        logger.LogInformation("NotificationService pronto. Aguardando mensagens...");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task WaitForRabbitMqAsync(ILogger logger, CancellationToken ct)
    {
        var factory = new ConnectionFactory { HostName = _rabbitHost };

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var connection = await factory.CreateConnectionAsync(ct);
                logger.LogInformation("Conectado ao RabbitMQ!");
                return;
            }
            catch
            {
                logger.LogWarning("RabbitMQ não está pronto. Tentando novamente em 3s...");
                await Task.Delay(3_000, ct);
            }
        }
    }
}
```

**O Worker ficou muito mais simples**: apenas 3 canais (setup, consumo, publicação) e um único consumer.

### 7.6 Configurar o Program.cs

Substitua `NotificationService/Program.cs`:

```csharp
using NotificationService;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
```

O Program.cs também ficou mínimo — sem handlers, sem serviços de suporte. Toda essa responsabilidade migrou para os serviços dedicados.

---

## 8. Etapa 5 — EmailService (Worker de E-mail)

O EmailService é o primeiro dos três serviços dedicados de notificação. Ele:

1. Declara sua **própria topologia** no RabbitMQ (fila, retry queues, DLQ).
2. Consome da fila `notifications.email`.
3. Processa com retry e DLQ.
4. Garante idempotência.
5. Coleta métricas.

Os outros dois (PushService e SmsService) seguem exatamente a mesma estrutura, mudando apenas o nome, a routing key e a simulação de falha.

### 8.1 Instalar pacotes e referências

```powershell
cd EmailService
dotnet add package RabbitMQ.Client --version 7.2.1
dotnet add package Microsoft.Extensions.Hosting --version 8.0.1
dotnet add reference ../Contracts/Contracts.csproj
cd ..
```

### 8.2 Criar a estrutura de pastas

```powershell
mkdir EmailService/Messaging
mkdir EmailService/Services
```

### 8.3 Topologia do EmailService — RabbitMqTopology

Crie `EmailService/Messaging/RabbitMqTopology.cs`:

```csharp
using RabbitMQ.Client;

namespace EmailService.Messaging;

public static class RabbitMqTopology
{
    public const string NotificationsExchange = "notifications.exchange";
    public const string RetryExchange         = "notifications.email.retry.exchange";
    public const string ServiceQueue          = "notifications.email";
    public const string DlqQueue             = "notifications.email.dlq";
    public const string RoutingKey           = "email";

    public static readonly int[] RetryDelaysMs = [5_000, 15_000, 45_000];

    public static async Task DeclareAsync(IChannel channel)
    {
        await channel.ExchangeDeclareAsync(
            exchange: NotificationsExchange,
            type: ExchangeType.Direct,
            durable: true
        );

        await channel.ExchangeDeclareAsync(
            exchange: RetryExchange,
            type: ExchangeType.Direct,
            durable: true
        );

        await channel.QueueDeclareAsync(
            queue: ServiceQueue,
            durable: true,
            exclusive: false,
            autoDelete: false
        );
        await channel.QueueBindAsync(
            queue: ServiceQueue,
            exchange: NotificationsExchange,
            routingKey: RoutingKey
        );

        for (int i = 0; i < RetryDelaysMs.Length; i++)
        {
            var retryQueue = $"notifications.email.retry.{i + 1}";
            await channel.QueueDeclareAsync(
                queue: retryQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-message-ttl"]             = RetryDelaysMs[i],
                    ["x-dead-letter-exchange"]    = NotificationsExchange,
                    ["x-dead-letter-routing-key"] = RoutingKey
                }
            );
            await channel.QueueBindAsync(
                queue: retryQueue,
                exchange: RetryExchange,
                routingKey: $"retry.{i + 1}"
            );
        }

        await channel.QueueDeclareAsync(
            queue: DlqQueue,
            durable: true,
            exclusive: false,
            autoDelete: false
        );
    }
}
```

**Cada serviço tem sua PRÓPRIA topologia isolada.** Compare:

| Recurso | EmailService | PushService | SmsService |
|---------|-------------|-------------|------------|
| Fila principal | `notifications.email` | `notifications.push` | `notifications.sms` |
| Exchange de retry | `notifications.email.retry.exchange` | `notifications.push.retry.exchange` | `notifications.sms.retry.exchange` |
| Filas de retry | `notifications.email.retry.1/2/3` | `notifications.push.retry.1/2/3` | `notifications.sms.retry.1/2/3` |
| DLQ | `notifications.email.dlq` | `notifications.push.dlq` | `notifications.sms.dlq` |
| Routing key | `"email"` | `"push"` | `"sms"` |

Todos compartilham a mesma `notifications.exchange` — ela é declarada idempotentemente por cada serviço.

**Detalhe crítico — `x-dead-letter-routing-key`:**

```csharp
["x-dead-letter-routing-key"] = RoutingKey   // "email" — crítico!
```

Quando uma mensagem expira na fila de retry (TTL vence), o RabbitMQ a move para a exchange configurada em `x-dead-letter-exchange` com a routing key de `x-dead-letter-routing-key`. Se essa routing key fosse errada (ex: `"notification"` em vez de `"email"`), a mensagem **desapareceria silenciosamente** — nenhuma fila teria binding para ela.

#### Diagrama da topologia do EmailService

```text
              ┌─────────────────────────────┐
              │     notifications.exchange   │  (compartilhada por todos)
              │         (DIRECT)             │
              └───────────┬─────────────────┘
                          │ routing key: "email"
                          ▼
              ┌─────────────────────────────┐
              │  notifications.email (fila) │◄──── mensagens novas
              │                             │◄──── mensagens que voltaram do retry
              └───────────┬─────────────────┘
                          │ consumer lê
                          ▼
                   ┌──────────────┐
                   │   Sucesso?   │
                   └──────┬───────┘
                     Sim  │   Não
                     │    │
                     ▼    ▼
                   [OK] ┌─────────────────┐
                        │ RetryCount ≤ 3? │
                        └───────┬─────────┘
                          Sim   │   Não
                          │     │
                          ▼     ▼
  ┌─────────────────────────────┐  ┌──────────────────────────┐
  │ notifications.email.retry.  │  │  notifications.email.dlq │
  │ exchange (DIRECT)           │  │  (fila final)            │
  └───────────┬─────────────────┘  └──────────────────────────┘
              │ routing key:
              │ "retry.1" / "retry.2" / "retry.3"
              ▼
  ┌──────────────────────────────────┐
  │ notifications.email.retry.1      │ ← TTL 5s
  │ notifications.email.retry.2      │ ← TTL 15s
  │ notifications.email.retry.3      │ ← TTL 45s
  └──────────┬───────────────────────┘
             │ quando o TTL expira, x-dead-letter-exchange
             │ = notifications.exchange, routing key = "email"
             ▼
  ┌─────────────────────────────┐
  │  notifications.email (fila) │  ← volta para a fila principal!
  └─────────────────────────────┘
```

### 8.4 NotificationConsumer do EmailService

Crie `EmailService/Messaging/NotificationConsumer.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Contracts;
using EmailService.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EmailService.Messaging;

public class NotificationConsumer
{
    private readonly IChannel _consumeChannel;
    private readonly IChannel _publishChannel;
    private readonly IdempotencyService _idempotency;
    private readonly MetricsService _metrics;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _publishLock = new(1, 1);

    public NotificationConsumer(
        IChannel consumeChannel,
        IChannel publishChannel,
        IdempotencyService idempotency,
        MetricsService metrics,
        ILogger logger)
    {
        _consumeChannel = consumeChannel;
        _publishChannel = publishChannel;
        _idempotency    = idempotency;
        _metrics        = metrics;
        _logger         = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await _consumeChannel.BasicQosAsync(
            prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: ct
        );

        var consumer = new AsyncEventingBasicConsumer(_consumeChannel);
        consumer.ReceivedAsync += HandleNotificationAsync;

        await _consumeChannel.BasicConsumeAsync(
            queue: RabbitMqTopology.ServiceQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: ct
        );

        _logger.LogInformation("[EmailService] Aguardando notificações...");
    }

    private async Task HandleNotificationAsync(object _, BasicDeliverEventArgs args)
    {
        var json = Encoding.UTF8.GetString(args.Body.ToArray());
        var notification = JsonSerializer.Deserialize<NotificationMessage>(json)!;

        if (!_idempotency.TryMarkProcessed(notification.NotificationId))
        {
            _logger.LogWarning("[EmailService] Duplicata ignorada: {Id}", notification.NotificationId);
            await _consumeChannel.BasicAckAsync(args.DeliveryTag, multiple: false);
            return;
        }

        var success = await SimulateEmailAsync(notification);

        if (success)
        {
            _metrics.RecordSuccess("email");
            _logger.LogInformation(
                "[EmailService] Enviado para {Recipient} (pedido {OrderId})",
                notification.Recipient, notification.OrderId
            );
            await _consumeChannel.BasicAckAsync(args.DeliveryTag, multiple: false);
            return;
        }

        var nextRetry = notification.RetryCount + 1;

        if (nextRetry <= RabbitMqTopology.RetryDelaysMs.Length)
        {
            _logger.LogWarning("[EmailService] [RETRY {Retry}/3] {Id}", nextRetry, notification.NotificationId);
            _metrics.RecordFailure();

            var retryMsg = notification with { RetryCount = nextRetry };
            var body     = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(retryMsg));
            var props    = new BasicProperties { Persistent = true };

            await _publishLock.WaitAsync();
            try
            {
                await _publishChannel.BasicPublishAsync(
                    exchange: RabbitMqTopology.RetryExchange,
                    routingKey: $"retry.{nextRetry}",
                    mandatory: false,
                    basicProperties: props,
                    body: body
                );
            }
            finally { _publishLock.Release(); }
        }
        else
        {
            _logger.LogError("[EmailService] [DLQ] Esgotadas tentativas: {Id}", notification.NotificationId);
            _metrics.RecordDlq();

            var body  = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(notification));
            var props = new BasicProperties { Persistent = true };

            await _publishLock.WaitAsync();
            try
            {
                await _publishChannel.BasicPublishAsync(
                    exchange: "",
                    routingKey: RabbitMqTopology.DlqQueue,
                    mandatory: false,
                    basicProperties: props,
                    body: body
                );
            }
            finally { _publishLock.Release(); }
        }

        await _consumeChannel.BasicAckAsync(args.DeliveryTag, multiple: false);
    }

    private static async Task<bool> SimulateEmailAsync(NotificationMessage notification)
    {
        await Task.Delay(Random.Shared.Next(50, 150));
        return Random.Shared.NextDouble() >= 0.30;
    }
}
```

**Diferença em relação à versão anterior:** a simulação agora está **embutida no consumer** (`SimulateEmailAsync`) em vez de em handlers separados. Faz sentido porque cada serviço lida com exatamente um tipo de notificação — não há necessidade do padrão Strategy aqui.

**O SemaphoreSlim** garante que apenas uma publicação aconteça por vez no canal de publicação, já que `BasicPublishAsync` não é thread-safe.

### 8.5 Serviços de suporte

#### IdempotencyService

Crie `EmailService/Services/IdempotencyService.cs`:

```csharp
using System.Collections.Concurrent;

namespace EmailService.Services;

public class IdempotencyService
{
    private readonly ConcurrentDictionary<Guid, byte> _processed = new();

    public bool TryMarkProcessed(Guid notificationId) =>
        _processed.TryAdd(notificationId, 0);

    public void Cleanup() => _processed.Clear();
}
```

**Simplificação:** em vez de `AlreadyProcessed` + `Register` em dois métodos, agora é um único `TryMarkProcessed` que retorna `true` se é novo (adicionou com sucesso) e `false` se é duplicata (já existia). Mais conciso e atômico.

#### MetricsService

Crie `EmailService/Services/MetricsService.cs`:

```csharp
using System.Collections.Concurrent;

namespace EmailService.Services;

public class MetricsService
{
    private long _totalProcessed;
    private long _totalFailed;
    private long _totalDlq;
    private readonly ConcurrentDictionary<string, long> _successByType = new();

    public void RecordSuccess(string type)
    {
        Interlocked.Increment(ref _totalProcessed);
        _successByType.AddOrUpdate(type, 1, (_, v) => Interlocked.Increment(ref v));
    }

    public void RecordFailure() => Interlocked.Increment(ref _totalFailed);
    public void RecordDlq()     => Interlocked.Increment(ref _totalDlq);

    public void LogSummary(ILogger logger)
    {
        logger.LogInformation(
            "[Métricas] Processadas: {Processed} | Falhas: {Failed} | DLQ: {Dlq}",
            _totalProcessed, _totalFailed, _totalDlq
        );
    }
}
```

### 8.6 Worker do EmailService

Crie `EmailService/Worker.cs`:

```csharp
using EmailService.Messaging;
using EmailService.Services;
using RabbitMQ.Client;

namespace EmailService;

public class Worker : BackgroundService
{
    private readonly IdempotencyService _idempotency;
    private readonly MetricsService _metrics;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _rabbitHost;

    public Worker(
        IdempotencyService idempotency,
        MetricsService metrics,
        ILoggerFactory loggerFactory,
        IConfiguration config)
    {
        _idempotency   = idempotency;
        _metrics       = metrics;
        _loggerFactory = loggerFactory;
        _rabbitHost    = config["RabbitMQ:Host"] ?? "localhost";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var logger = _loggerFactory.CreateLogger<Worker>();

        await WaitForRabbitMqAsync(logger, stoppingToken);

        var factory = new ConnectionFactory { HostName = _rabbitHost };
        await using var connection = await factory.CreateConnectionAsync(stoppingToken);

        await using var setupChannel   = await connection.CreateChannelAsync(
            cancellationToken: stoppingToken);
        await using var consumeChannel = await connection.CreateChannelAsync(
            cancellationToken: stoppingToken);
        await using var publishChannel = await connection.CreateChannelAsync(
            cancellationToken: stoppingToken);

        await RabbitMqTopology.DeclareAsync(setupChannel);
        logger.LogInformation("[EmailService] Topologia declarada.");

        var consumer = new NotificationConsumer(
            consumeChannel,
            publishChannel,
            _idempotency,
            _metrics,
            _loggerFactory.CreateLogger<NotificationConsumer>()
        );
        await consumer.StartAsync(stoppingToken);

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _metrics.LogSummary(logger);
                _idempotency.Cleanup();
            }
        }, stoppingToken);

        logger.LogInformation("[EmailService] Pronto. Aguardando notificações de email...");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task WaitForRabbitMqAsync(ILogger logger, CancellationToken ct)
    {
        var factory = new ConnectionFactory { HostName = _rabbitHost };

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var connection = await factory.CreateConnectionAsync(ct);
                logger.LogInformation("[EmailService] Conectado ao RabbitMQ!");
                return;
            }
            catch
            {
                logger.LogWarning("[EmailService] RabbitMQ não está pronto. Tentando novamente em 3s...");
                await Task.Delay(3_000, ct);
            }
        }
    }
}
```

### 8.7 Program.cs do EmailService

Substitua `EmailService/Program.cs`:

```csharp
using EmailService;
using EmailService.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<IdempotencyService>();
builder.Services.AddSingleton<MetricsService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
```

### 8.8 appsettings.json do EmailService

`EmailService/appsettings.json`:

```json
{
  "RabbitMQ": {
    "Host": "localhost"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

---

## 9. Etapa 6 — PushService e SmsService

O PushService e o SmsService seguem **exatamente a mesma estrutura** do EmailService. As únicas diferenças são:

| Aspecto | EmailService | PushService | SmsService |
|---------|-------------|-------------|------------|
| Namespace | `EmailService.*` | `PushService.*` | `SmsService.*` |
| Routing key | `"email"` | `"push"` | `"sms"` |
| Fila principal | `notifications.email` | `notifications.push` | `notifications.sms` |
| DLQ | `notifications.email.dlq` | `notifications.push.dlq` | `notifications.sms.dlq` |
| Exchange de retry | `notifications.email.retry.exchange` | `notifications.push.retry.exchange` | `notifications.sms.retry.exchange` |
| Taxa de falha simulada | 30% (SMTP instável) | 20% (token expirado) | 40% (operadora instável) |
| Latência simulada | 50-150ms | 20-80ms | 100-300ms |

### 9.1 Criar o PushService

Repita os mesmos passos do EmailService, substituindo:
- Namespace: `EmailService` → `PushService`
- Routing key e nomes de filas: `email` → `push`
- Simulação de falha: `SimulatePushAsync` com 20% de falha e 20-80ms de latência

```powershell
cd PushService
dotnet add package RabbitMQ.Client --version 7.2.1
dotnet add package Microsoft.Extensions.Hosting --version 8.0.1
dotnet add reference ../Contracts/Contracts.csproj
cd ..
```

No `NotificationConsumer` do PushService, a simulação muda para:

```csharp
private static async Task<bool> SimulatePushAsync(NotificationMessage notification)
{
    await Task.Delay(Random.Shared.Next(20, 80));
    return Random.Shared.NextDouble() >= 0.20;
}
```

### 9.2 Criar o SmsService

Mesma coisa, substituindo `email` → `sms` e com 40% de falha e 100-300ms de latência:

```powershell
cd SmsService
dotnet add package RabbitMQ.Client --version 7.2.1
dotnet add package Microsoft.Extensions.Hosting --version 8.0.1
dotnet add reference ../Contracts/Contracts.csproj
cd ..
```

No `NotificationConsumer` do SmsService:

```csharp
private static async Task<bool> SimulateSmsAsync(NotificationMessage notification)
{
    await Task.Delay(Random.Shared.Next(100, 300));
    return Random.Shared.NextDouble() >= 0.40;
}
```

O SMS tem a maior taxa de falha (40%) porque, na vida real, operadoras de telecomunicações são historicamente o canal menos confiável.

---

## 10. Etapa 7 — Docker e Docker Compose

### 10.1 Dockerfiles — Build Multi-Projeto

Como todos os serviços referenciam o projeto `Contracts`, os Dockerfiles precisam do **contexto da raiz** para acessar ambos os projetos durante o build.

#### Dockerfile do OrderService

Crie `OrderService/Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY Contracts/Contracts.csproj Contracts/
COPY OrderService/OrderService.csproj OrderService/
RUN dotnet restore OrderService/OrderService.csproj
COPY Contracts/ Contracts/
COPY EmailService/ EmailService/
RUN dotnet publish OrderService/OrderService.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "OrderService.dll"]
```

**Diferença em relação ao Dockerfile anterior:** agora copiamos `Contracts/` antes do serviço, porque o `OrderService.csproj` tem um `ProjectReference` para `Contracts.csproj`. O Docker precisa de ambos para compilar.

**Ordem das instruções COPY (otimização de cache):**

1. Primeiro copiamos só os `.csproj` e fazemos `restore` — isso cacheia os pacotes NuGet.
2. Depois copiamos o código fonte e compilamos — se só o código mudar, o restore não é refeito.

#### Dockerfile do NotificationService

Crie `NotificationService/Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY Contracts/Contracts.csproj Contracts/
COPY NotificationService/NotificationService.csproj NotificationService/
RUN dotnet restore NotificationService/NotificationService.csproj
COPY Contracts/ Contracts/
COPY NotificationService/ NotificationService/
RUN dotnet publish NotificationService/NotificationService.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "NotificationService.dll"]
```

#### Dockerfile do EmailService (PushService e SmsService são idênticos, mudando o nome)

Crie `EmailService/Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY Contracts/Contracts.csproj Contracts/
COPY EmailService/EmailService.csproj EmailService/
RUN dotnet restore EmailService/EmailService.csproj
COPY Contracts/ Contracts/
COPY EmailService/ EmailService/
RUN dotnet publish EmailService/EmailService.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "EmailService.dll"]
```

Repita para `PushService/Dockerfile` e `SmsService/Dockerfile`, trocando `EmailService` pelo nome correspondente.

### 10.2 Docker Compose — Orquestrando 6 Serviços

Crie `docker-compose.yml` na raiz do projeto:

```yaml
version: '3.8'

services:
  rabbitmq:
    image: rabbitmq:3-management
    container_name: rabbitmq
    ports:
      - "5672:5672"   # porta AMQP (sua app conecta aqui)
      - "15672:15672" # painel web de admin
    environment:
      RABBITMQ_DEFAULT_USER: guest
      RABBITMQ_DEFAULT_PASS: guest
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5
      start_period: 30s

  order-service:
    build:
      context: .
      dockerfile: OrderService/Dockerfile
    depends_on:
      rabbitmq:
        condition: service_healthy
    ports:
      - "5000:8080"   # acessa a API em http://localhost:5000
    environment:
      - RabbitMQ__Host=rabbitmq

  notification-service:
    build:
      context: .
      dockerfile: NotificationService/Dockerfile
    depends_on:
      rabbitmq:
        condition: service_healthy
    environment:
      - RabbitMQ__Host=rabbitmq

  email-service:
    build:
      context: .
      dockerfile: EmailService/Dockerfile
    depends_on:
      rabbitmq:
        condition: service_healthy
    environment:
      - RabbitMQ__Host=rabbitmq

  push-service:
    build:
      context: .
      dockerfile: PushService/Dockerfile
    depends_on:
      rabbitmq:
        condition: service_healthy
    environment:
      - RabbitMQ__Host=rabbitmq

  sms-service:
    build:
      context: .
      dockerfile: SmsService/Dockerfile
    depends_on:
      rabbitmq:
        condition: service_healthy
    environment:
      - RabbitMQ__Host=rabbitmq
```

**Novidades importantes:**

#### Healthcheck no RabbitMQ

```yaml
healthcheck:
  test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
  interval: 10s
  timeout: 5s
  retries: 5
  start_period: 30s
```

Antes, usávamos apenas `depends_on: rabbitmq`, que só garante que o **container** do RabbitMQ iniciou — mas não que ele está **pronto para aceitar conexões**. Agora, o Docker verifica a saúde do RabbitMQ com o comando `rabbitmq-diagnostics ping` a cada 10 segundos. Os outros serviços só iniciam quando o RabbitMQ responde `service_healthy`.

#### Build context na raiz

```yaml
build:
  context: .
  dockerfile: OrderService/Dockerfile
```

O `context: .` define que o build Docker roda a partir da **raiz do projeto** (não do subdiretório do serviço). Isso é necessário porque os Dockerfiles precisam copiar o projeto `Contracts/` que está fora do diretório do serviço.

---

## 11. Etapa 8 — Testes Unitários

### 11.1 Instalar pacotes

```powershell
cd OrderService.Tests
dotnet add package Moq --version 4.20.72
dotnet add reference ../OrderService/OrderService.csproj
cd ..
```

### 11.2 Testes do Controller

Crie a pasta e o arquivo `OrderService.Tests/Controllers/OrdersControllerTests.cs`:

```csharp
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
```

**Diferenças em relação à versão anterior:**

1. **Sem Mock!** O controller agora recebe `OutboxStore` diretamente (não uma interface). Como o OutboxStore é simples e sem efeitos colaterais externos, podemos usar uma instância real no teste.
2. **Testes síncronos:** como o controller agora é síncrono, os testes também são (`void` em vez de `async Task`).
3. **Verificação pelo OutboxStore:** em vez de `_publisherMock.Verify(...)`, verificamos diretamente que a mensagem está no outbox com `_outbox.Count` e `_outbox.TryPeek`.

### 11.3 Testes do OutboxStore

Crie `OrderService.Tests/Outbox/OutboxStoreTests.cs`:

```csharp
using Contracts;
using OrderService.Outbox;

namespace OrderService.Tests.Outbox;

public class OutboxStoreTests
{
    [Fact]
    public void Enqueue_AdicionaMensagem_CountAumenta()
    {
        var store = new OutboxStore();
        var message = new OrderCreatedMessage { OrderId = Guid.NewGuid() };

        store.Enqueue(message);

        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void TryPeek_RetornaMensagem_SemRemover()
    {
        var store = new OutboxStore();
        var message = new OrderCreatedMessage { OrderId = Guid.NewGuid() };
        store.Enqueue(message);

        var found = store.TryPeek(out var peeked);

        Assert.True(found);
        Assert.Equal(message.OrderId, peeked!.OrderId);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void TryDequeue_RetornaMensagem_ERemove()
    {
        var store = new OutboxStore();
        var message = new OrderCreatedMessage { OrderId = Guid.NewGuid() };
        store.Enqueue(message);

        var found = store.TryDequeue(out var dequeued);

        Assert.True(found);
        Assert.Equal(message.OrderId, dequeued!.OrderId);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void TryPeek_FilaVazia_RetornaFalse()
    {
        var store = new OutboxStore();

        var found = store.TryPeek(out var message);

        Assert.False(found);
        Assert.Null(message);
    }

    [Fact]
    public void Enqueue_MultiplasMensagens_MantemOrdem()
    {
        var store = new OutboxStore();
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();

        foreach (var id in ids)
            store.Enqueue(new OrderCreatedMessage { OrderId = id });

        foreach (var id in ids)
        {
            store.TryDequeue(out var msg);
            Assert.Equal(id, msg!.OrderId);
        }
    }

    [Fact]
    public void ThreadSafety_EnqueueConcorrente_NaoPerdeEntradas()
    {
        var store = new OutboxStore();
        var threads = 10;
        var messagesPerThread = 100;

        Parallel.For(0, threads, _ =>
        {
            for (int i = 0; i < messagesPerThread; i++)
                store.Enqueue(new OrderCreatedMessage { OrderId = Guid.NewGuid() });
        });

        Assert.Equal(threads * messagesPerThread, store.Count);
    }
}
```

**Destaque: o teste de thread safety.** O `Parallel.For` cria 10 threads que enfileiram 100 mensagens cada uma simultaneamente. Se o `ConcurrentQueue` não fosse thread-safe, poderíamos perder entradas. O assert garante que todas as 1.000 mensagens foram enfileiradas.

### 11.4 Testes do OutboxPublisher

Crie `OrderService.Tests/Outbox/OutboxPublisherTests.cs`:

```csharp
using Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderService.Messaging;
using OrderService.Outbox;

namespace OrderService.Tests.Outbox;

public class OutboxPublisherTests
{
    private readonly Mock<IRabbitMqPublisher> _publisherMock = new();
    private readonly OutboxStore _store = new();

    [Fact]
    public async Task Publisher_PublicaMensagem_ERemoveDoStore()
    {
        var message = new OrderCreatedMessage { OrderId = Guid.NewGuid() };
        _store.Enqueue(message);

        _publisherMock
            .Setup(p => p.PublishAsync(It.IsAny<OrderCreatedMessage>()))
            .Returns(Task.CompletedTask);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var publisher = new OutboxPublisher(
            _store, _publisherMock.Object, NullLogger<OutboxPublisher>.Instance
        );

        _ = publisher.StartAsync(cts.Token);
        await Task.Delay(1_500, CancellationToken.None);

        _publisherMock.Verify(p => p.PublishAsync(It.IsAny<OrderCreatedMessage>()), Times.Once);
        Assert.Equal(0, _store.Count);

        await publisher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Publisher_QuandoPublishFalha_NaoRemoveMensagemDoStore()
    {
        var message = new OrderCreatedMessage { OrderId = Guid.NewGuid() };
        _store.Enqueue(message);

        _publisherMock
            .Setup(p => p.PublishAsync(It.IsAny<OrderCreatedMessage>()))
            .ThrowsAsync(new Exception("RabbitMQ indisponível"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var publisher = new OutboxPublisher(
            _store, _publisherMock.Object, NullLogger<OutboxPublisher>.Instance
        );

        _ = publisher.StartAsync(cts.Token);
        await Task.Delay(2_500, CancellationToken.None);

        Assert.Equal(1, _store.Count);

        await publisher.StopAsync(CancellationToken.None);
    }
}
```

**Esses testes validam a garantia mais crítica do Outbox:**

- **Teste 1**: quando o publish é bem-sucedido, a mensagem é removida do store.
- **Teste 2**: quando o publish falha, a mensagem **permanece** no store para retry. Se ela fosse removida mesmo em falha, perderíamos a mensagem.

---

## 12. Etapa 9 — Testes de Integração

Os testes de integração verificam que os serviços funcionam com infraestrutura real. Usamos **Testcontainers** para subir um RabbitMQ de verdade em um container Docker durante os testes.

### 12.1 Instalar pacotes

```powershell
cd Integration.Tests
dotnet add package RabbitMQ.Client --version 7.2.1
dotnet add package Testcontainers.RabbitMq --version 4.11.0
dotnet add reference ../OrderService/OrderService.csproj
cd ..
```

### 12.2 Criar os testes

Crie `Integration.Tests/OrderCreatedFlowTests.cs`:

```csharp
using System.Text;
using System.Text.Json;
using OrderService.Messaging;
using Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Testcontainers.RabbitMq;

namespace Integration.Tests;

public class OrderCreatedFlowTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbitMqContainer;
    private const string QueueName = "orders";

    public OrderCreatedFlowTests()
    {
        _rabbitMqContainer = new RabbitMqBuilder("rabbitmq:3-management")
            .WithUsername("guest")
            .WithPassword("guest")
            .Build();
    }

    public async Task InitializeAsync() =>
        await _rabbitMqContainer.StartAsync();

    public async Task DisposeAsync() =>
        await _rabbitMqContainer.DisposeAsync();

    [Fact]
    public async Task Publicar_UmaMensagem_DeveChegar_NaFila()
    {
        var host = _rabbitMqContainer.Hostname;
        var port = _rabbitMqContainer.GetMappedPublicPort(5672);

        var publisher = await RabbitMqPublisher.CreateAsync(host, port);

        var mensagem = new OrderCreatedMessage
        {
            OrderId = Guid.NewGuid(),
            CustomerName = "Teste Integração",
            TotalAmount = 100m,
            CreatedAt = DateTime.UtcNow
        };

        await publisher.PublishAsync(mensagem);

        var factory = new ConnectionFactory { HostName = host, Port = port };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.QueueDeclareAsync(
            QueueName, durable: true, exclusive: false, autoDelete: false
        );

        var tcs = new TaskCompletionSource<OrderCreatedMessage>();
        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += (_, args) =>
        {
            var json = Encoding.UTF8.GetString(args.Body.ToArray());
            var received = JsonSerializer.Deserialize<OrderCreatedMessage>(json)!;
            tcs.SetResult(received);
            return Task.CompletedTask;
        };

        await channel.BasicConsumeAsync(QueueName, autoAck: true, consumer: consumer);

        var mensagemRecebida = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(mensagem.OrderId, mensagemRecebida.OrderId);
        Assert.Equal(mensagem.CustomerName, mensagemRecebida.CustomerName);
        Assert.Equal(mensagem.TotalAmount, mensagemRecebida.TotalAmount);

        await publisher.DisposeAsync();
    }

    [Fact]
    public async Task Publicar_MultiplasMensagens_TodasDevemChegar_NaFila()
    {
        var host = _rabbitMqContainer.Hostname;
        var port = _rabbitMqContainer.GetMappedPublicPort(5672);

        var publisher = await RabbitMqPublisher.CreateAsync(host, port);
        var mensagens = Enumerable.Range(1, 3).Select(i => new OrderCreatedMessage
        {
            OrderId = Guid.NewGuid(),
            CustomerName = $"Cliente {i}",
            TotalAmount = i * 10m,
            CreatedAt = DateTime.UtcNow
        }).ToList();

        foreach (var msg in mensagens)
            await publisher.PublishAsync(msg);

        var factory = new ConnectionFactory { HostName = host, Port = port };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.QueueDeclareAsync(
            QueueName, durable: true, exclusive: false, autoDelete: false
        );

        var recebidas = new List<OrderCreatedMessage>();
        var tcs = new TaskCompletionSource();
        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += (_, args) =>
        {
            var json = Encoding.UTF8.GetString(args.Body.ToArray());
            recebidas.Add(JsonSerializer.Deserialize<OrderCreatedMessage>(json)!);
            if (recebidas.Count == mensagens.Count) tcs.SetResult();
            return Task.CompletedTask;
        };

        await channel.BasicConsumeAsync(QueueName, autoAck: true, consumer: consumer);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, recebidas.Count);
        Assert.All(mensagens, m =>
            Assert.Contains(recebidas, r => r.OrderId == m.OrderId)
        );

        await publisher.DisposeAsync();
    }
}
```

**O que é Testcontainers?** Uma biblioteca que cria e gerencia containers Docker durante os testes. Ela sobe um RabbitMQ real, executa os testes contra ele, e o derruba automaticamente. É como ter infraestrutura descartável.

**`IAsyncLifetime`**: interface do xUnit que fornece `InitializeAsync` (antes do teste) e `DisposeAsync` (depois do teste).

**`TaskCompletionSource`**: cria um `Task` que completamos manualmente quando a mensagem chega.

---

## 13. Etapa 10 — Rodando o Projeto Completo

### 13.1 Subir o ambiente

Na raiz do projeto:

```powershell
docker compose up --build
```

Agora o Docker vai construir **6 imagens** e subir **6 containers** (RabbitMQ + 5 serviços .NET).

**Espere até ver nos logs:**

```text
notification-service  | NotificationService pronto. Aguardando mensagens...
email-service         | [EmailService] Pronto. Aguardando notificações de email...
push-service          | [PushService] Pronto. Aguardando notificações de push...
sms-service           | [SmsService] Pronto. Aguardando notificações de SMS...
```

### 13.2 Criar um pedido

Abra **outro terminal**:

**PowerShell:**

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5000/orders" `
  -ContentType "application/json" `
  -Body '{"customerName": "Joao Silva", "totalAmount": 150.90}'
```

**curl (Git Bash ou Linux):**

```bash
curl -X POST http://localhost:5000/orders \
  -H "Content-Type: application/json" \
  -d '{"customerName": "Joao Silva", "totalAmount": 150.90}'
```

**Resposta esperada:**

```json
{
  "orderId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "status": "Pedido recebido e sendo processado"
}
```

### 13.3 O que observar nos logs

```text
order-service         | Pedido a1b2c3d4-... gravado no Outbox
notification-service  | [OrderConsumer] Pedido a1b2c3d4-... → 3 notificações despachadas (email, push, sms)
email-service         | [EmailService] Enviado para joao.silva@example.com (pedido a1b2c3d4-...)
push-service          | [PushService] Enviado para device-a1b2c3d4 (pedido a1b2c3d4-...)
sms-service           | [SmsService] [RETRY 1/3] ...
sms-service           | [SmsService] Enviado para +55 11 9 0000-0000 (pedido a1b2c3d4-...)
```

Cada serviço agora loga com seu próprio prefixo (`[EmailService]`, `[PushService]`, `[SmsService]`), facilitando identificar qual serviço processou cada notificação.

### 13.4 Painel do RabbitMQ

Acesse: http://localhost:15672 (usuário: `guest`, senha: `guest`)

No painel, em **Queues**, você verá agora muito mais filas:

- `orders`
- `notifications.email`, `notifications.push`, `notifications.sms`
- `notifications.email.retry.1/2/3`, `notifications.push.retry.1/2/3`, `notifications.sms.retry.1/2/3`
- `notifications.email.dlq`, `notifications.push.dlq`, `notifications.sms.dlq`

### 13.5 Verificar Health Check

```powershell
Invoke-RestMethod http://localhost:5000/health
```

Deve retornar `Healthy`.

### 13.6 Rodar os testes

```powershell
dotnet test
```

### 13.7 Parar o ambiente

```powershell
docker compose down
```

---

## 14. Glossário

| Termo | Definição |
|-------|-----------|
| **AMQP** | Advanced Message Queuing Protocol — protocolo de mensageria usado pelo RabbitMQ |
| **API** | Application Programming Interface — interface para comunicação entre programas |
| **Async/Await** | Padrão de programação assíncrona em C# — permite esperar por operações longas sem travar |
| **Backoff Exponencial** | Estratégia de retry onde o tempo de espera cresce entre tentativas (5s, 15s, 45s) |
| **Container** | Ambiente isolado e portátil para rodar aplicações (gerenciado pelo Docker) |
| **Consumer** | Programa que lê e processa mensagens de uma fila |
| **DI (Dependency Injection)** | Padrão onde objetos recebem suas dependências externamente |
| **Dispatcher** | Componente que recebe uma mensagem e a direciona para o destino correto |
| **DLQ (Dead Letter Queue)** | Fila para mensagens que falharam e não serão mais retentadas |
| **DTO (Data Transfer Object)** | Objeto usado apenas para transportar dados entre camadas |
| **Exchange** | Componente do RabbitMQ que roteia mensagens para filas |
| **GUID** | Globally Unique Identifier — identificador único universal |
| **Healthcheck** | Verificação periódica de que um serviço está funcionando |
| **HTTP 202 Accepted** | "Recebi seu pedido, mas ainda estou processando" |
| **Idempotência** | Propriedade de uma operação que, executada múltiplas vezes, produz o mesmo resultado |
| **JSON** | JavaScript Object Notation — formato leve de troca de dados |
| **Microserviço** | Serviço pequeno e independente que faz uma coisa bem feita |
| **Mock** | Objeto falso usado em testes para simular dependências |
| **Multi-stage build** | Técnica de Dockerfile com múltiplos estágios para imagens menores |
| **Outbox Pattern** | Padrão onde mensagens são gravadas localmente antes de serem enviadas ao broker |
| **Producer** | Programa que envia mensagens para uma fila |
| **Queue (Fila)** | Estrutura onde mensagens ficam armazenadas até serem consumidas |
| **Record** | Tipo em C# otimizado para dados imutáveis com suporte a `with` |
| **REST** | Representational State Transfer — estilo arquitetural para APIs HTTP |
| **Routing Key** | Chave usada pela exchange para decidir para qual fila enviar a mensagem |
| **SDK** | Software Development Kit — kit de ferramentas para desenvolver aplicações |
| **Serialização** | Converter um objeto em texto (JSON) ou bytes para transmissão |
| **Shared Contracts** | Modelos compartilhados entre microserviços via projeto comum |
| **Singleton** | Padrão onde apenas uma instância de uma classe existe durante toda a vida da aplicação |
| **TTL** | Time To Live — tempo máximo que uma mensagem pode ficar em uma fila |
| **Worker** | Serviço em segundo plano que roda continuamente executando tarefas |
| **xUnit** | Framework de testes para .NET |
| **YAML** | Yet Another Markup Language — formato de configuração usado pelo Docker Compose |

---

## 15. Diagrama Completo da Arquitetura

```text
╔══════════════════════════════════════════════════════════════════════════════════════╗
║                           MICROSERVICES-DEMO — VISÃO GERAL                         ║
╚══════════════════════════════════════════════════════════════════════════════════════╝

  ┌──────────────────┐
  │   Cliente HTTP    │
  │  (Postman/curl)   │
  └────────┬─────────┘
           │ POST /orders
           ▼
  ┌──────────────────────────────────────────────────┐
  │              OrderService (porta 5000)            │
  │                                                   │
  │  Controller ──▶ OutboxStore ──▶ OutboxPublisher   │
  │  (síncrono)     (in-memory)     (background 1s)   │
  │                                   │               │
  │                                   ▼               │
  │                           RabbitMqPublisher        │
  └───────────────────────────────┬───────────────────┘
                                  │ exchange: "" / routingKey: "orders"
                                  ▼
╔══════════════════════════════════════════════════════════════════════════════╗
║                              RABBITMQ                                      ║
║                     (Container Docker + healthcheck)                       ║
║                                                                            ║
║  ┌──────────┐                                                              ║
║  │  orders   │                                                             ║
║  └─────┬────┘                                                              ║
║        │ (consumido pelo NotificationService)                               ║
║        ▼                                                                   ║
║  ┌─────────────────────────┐                                               ║
║  │  notifications.exchange │  (DIRECT)                                     ║
║  └─────┬──────┬──────┬────┘                                               ║
║        │      │      │    routing keys: "email", "push", "sms"            ║
║        ▼      ▼      ▼                                                    ║
║  ┌─────────┐ ┌─────────┐ ┌─────────┐                                     ║
║  │notif.   │ │notif.   │ │notif.   │                                     ║
║  │email    │ │push     │ │sms      │                                     ║
║  └────┬────┘ └────┬────┘ └────┬────┘                                     ║
║       │           │           │                                           ║
║  (cada serviço tem suas próprias retry queues e DLQ)                      ║
║                                                                            ║
║  EmailService:  retry.1(5s) → retry.2(15s) → retry.3(45s) → email.dlq   ║
║  PushService:   retry.1(5s) → retry.2(15s) → retry.3(45s) → push.dlq    ║
║  SmsService:    retry.1(5s) → retry.2(15s) → retry.3(45s) → sms.dlq     ║
╚══════════════════════════════════════════════════════════════════════════════╝
           │                    │                    │
           ▼                    ▼                    ▼
  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐
  │ EmailService │    │ PushService  │    │  SmsService  │
  │ (30% falha)  │    │ (20% falha)  │    │ (40% falha)  │
  │              │    │              │    │              │
  │ - Consumer   │    │ - Consumer   │    │ - Consumer   │
  │ - Idempotênc.│    │ - Idempotênc.│    │ - Idempotênc.│
  │ - Métricas   │    │ - Métricas   │    │ - Métricas   │
  │ - Retry/DLQ  │    │ - Retry/DLQ  │    │ - Retry/DLQ  │
  └──────────────┘    └──────────────┘    └──────────────┘

  ┌──────────────────────────────────────────────────┐
  │              NotificationService                  │
  │              (Dispatcher puro)                    │
  │                                                   │
  │  OrderConsumer: lê "orders"                       │
  │  → gera 3 NotificationMessage (email, push, sms) │
  │  → publica em notifications.exchange              │
  │    com routingKey = tipo da notificação           │
  └──────────────────────────────────────────────────┘

  ┌──────────────────────────────────────────────────┐
  │                    Contracts                      │
  │              (Projeto compartilhado)              │
  │                                                   │
  │  - OrderCreatedMessage                            │
  │  - NotificationMessage                            │
  │                                                   │
  │  Referenciado por todos os serviços               │
  └──────────────────────────────────────────────────┘
```

---

## 16. Dicas e Próximos Passos

### O que você aprendeu

- Como criar uma **API Web** com ASP.NET Core e **Outbox Pattern**.
- Como criar **Worker Services** para processamento em segundo plano.
- Como usar **RabbitMQ** para comunicação assíncrona entre microserviços.
- Como implementar **retry com backoff exponencial** e **Dead Letter Queue** isoladas por serviço.
- Como garantir **idempotência** no processamento de mensagens.
- Como criar um **projeto de contratos compartilhados** entre microserviços.
- Como **containerizar** aplicações .NET com Docker (multi-projeto).
- Como configurar **healthchecks** no Docker Compose para dependências.
- Como **orquestrar** 6 containers com Docker Compose.
- Como escrever **testes unitários** com xUnit e Moq.
- Como escrever **testes de integração** com Testcontainers.
- Como usar **Injeção de Dependência** nativa do .NET.

### Ideias para evoluir o projeto

1. **Adicionar um banco de dados**: use Entity Framework Core com PostgreSQL para persistir os pedidos e o Outbox (garantia de atomicidade real).
2. **Implementar idempotência com Redis**: substitua o `ConcurrentDictionary` por Redis para funcionar com múltiplas instâncias.
3. **Adicionar health checks customizados**: crie health checks que verificam a conexão com o RabbitMQ.
4. **Usar OpenTelemetry**: adicione tracing distribuído para rastrear uma requisição por todos os serviços.
5. **Adicionar API Gateway**: coloque um gateway (como YARP ou Ocelot) na frente dos serviços.
6. **Implementar autenticação**: adicione JWT para proteger o endpoint de pedidos.
7. **Adicionar Swagger/OpenAPI**: documente a API com Swagger UI.
8. **Escalar horizontalmente**: suba múltiplas instâncias do EmailService e veja o RabbitMQ distribuindo as mensagens.
9. **Adicionar CI/CD**: configure GitHub Actions para rodar os testes automaticamente a cada push.
10. **Outbox com banco de dados**: implemente o Outbox Pattern real com Entity Framework + Transação.

### Comandos úteis de referência rápida

| Comando | O que faz |
|---------|-----------|
| `dotnet new sln -n Nome` | Cria uma solution |
| `dotnet new classlib -n Nome` | Cria uma biblioteca de classes |
| `dotnet new webapi -n Nome` | Cria um projeto de API Web |
| `dotnet new worker -n Nome` | Cria um projeto Worker |
| `dotnet new xunit -n Nome` | Cria um projeto de testes xUnit |
| `dotnet sln add Proj/Proj.csproj` | Adiciona um projeto à solution |
| `dotnet add package NomeDoPacote` | Instala um pacote NuGet |
| `dotnet add reference ../Outro/Outro.csproj` | Adiciona referência entre projetos |
| `dotnet build` | Compila a solution |
| `dotnet test` | Roda todos os testes |
| `dotnet run --project NomeDoProjeto` | Roda um projeto específico |
| `docker compose up --build` | Sobe o ambiente com Docker |
| `docker compose down` | Derruba o ambiente |
| `docker compose logs -f nome-servico` | Acompanha logs de um serviço |

---

**Parabéns!** Se você chegou até aqui, você construiu um sistema de microserviços com 6 projetos, comunicação assíncrona, retry resiliente, Outbox Pattern e infraestrutura containerizada. Isso não é pouca coisa. Agora use esse conhecimento como base para construir seus próprios projetos.

*Lembre-se: a melhor forma de aprender é fazendo. Não tenha medo de errar — cada erro é uma aula.*
