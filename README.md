# PaymentsAPI — FIAP Cloud Games

Microsserviço de **simulação de pagamentos** da plataforma FIAP Cloud Games (FCG), Fase 2: consome o pedido de compra, decide se o pagamento é aprovado ou rejeitado e publica o resultado de volta no barramento de eventos.

![CI](https://github.com/fcg-grupo-16/payments-api/actions/workflows/ci.yml/badge.svg)

---

## 1. Visão geral

O PaymentsAPI é um serviço **orientado a eventos** (event-driven). Ele não expõe API REST de negócio: todo o trabalho acontece a partir de mensagens trafegando pelo RabbitMQ.

O fluxo é:

```
CatalogAPI                         PaymentsAPI                       CatalogAPI / NotificationsAPI
   │                                   │                                        │
   │  publica OrderPlacedEvent         │                                        │
   ├──────────────────────────────────▶                                        │
   │                                   │  1. recebe o pedido                    │
   │                                   │  2. simula o processamento (delay)     │
   │                                   │  3. decide Approved / Rejected         │
   │                                   │                                        │
   │                                   │  publica PaymentProcessedEvent         │
   │                                   ├────────────────────────────────────────▶
```

1. **Consome** `OrderPlacedEvent` — emitido pelo CatalogAPI quando o usuário inicia a compra de um jogo. Campos:
   - `OrderId` (`Guid`) — identificador do pedido.
   - `UserId` (`string`) — identificador do usuário comprador.
   - `GameId` (`string`) — identificador do jogo.
   - `Price` (`decimal`) — valor do pedido.
2. **Decide** o resultado e simula um tempo fixo de processamento (delay de 500 ms) para tornar o demo realista.
3. **Publica** `PaymentProcessedEvent` com o resultado. Campos:
   - `OrderId` (`Guid`) — mesmo identificador do pedido.
   - `UserId` (`string`) — mesmo usuário.
   - `GameId` (`string`) — mesmo jogo.
   - `Price` (`decimal`) — mesmo valor.
   - `Status` (`string`) — `"Approved"` ou `"Rejected"`.

Os contratos de evento ficam no namespace **`Fcg.Contracts.Events`** e devem ser **idênticos em todos os serviços FCG** (o MassTransit identifica a mensagem pela URN derivada de `namespace:NomeDoTipo`).

### Regra de aprovação

A decisão é **determinística** (para tornar o demo reproduzível):

- `Approved` quando `Price <= Payments:MaxApprovedAmount` (padrão `5000`);
- `Rejected` caso contrário.

> Como o valor exatamente igual ao limite é aprovado, o limite padrão de `5000` aprova qualquer pedido de até R$ 5.000,00 inclusive.

---

## 2. Stack

- **.NET 10** — projeto único ASP.NET Core (minimal hosting) que hospeda o consumer e expõe `/health`.
- **RabbitMQ** — broker de mensagens.
- **MassTransit 8.x** — abstração de mensageria sobre o RabbitMQ.
- **xUnit + FluentAssertions** — testes unitários.
- **Docker** + **Kubernetes** — empacotamento e deploy.
- **Sem banco de dados.** O serviço é stateless; tudo é processado em memória a partir do evento recebido.

---

## 3. Arquitetura

Estrutura de pastas real do repositório:

```
payments-api/
├── src/
│   └── Fcg.Payments.Api/
│       ├── Consumers/
│       │   └── OrderPlacedConsumer.cs   # consome OrderPlacedEvent e publica PaymentProcessedEvent
│       ├── Contracts/
│       │   └── Events.cs                # contratos de evento (namespace Fcg.Contracts.Events)
│       ├── Payments/
│       │   ├── PaymentDecision.cs       # regra de decisão pura/testável
│       │   └── PaymentsOptions.cs       # opções da seção "Payments"
│       ├── Program.cs                   # bootstrap: MassTransit + RabbitMQ + /health
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       └── Fcg.Payments.Api.csproj
├── tests/
│   └── Fcg.Payments.UnitTests/
│       └── PaymentDecisionTests.cs      # cobre limites e fronteiras de PaymentDecision
├── k8s/
│   ├── configmap.yaml
│   ├── secret.yaml
│   ├── deployment.yaml
│   └── service.yaml
├── Dockerfile
├── global.json
└── PaymentsApi.sln
```

### `OrderPlacedConsumer`

Em `src/Fcg.Payments.Api/Consumers/OrderPlacedConsumer.cs`. É um `IConsumer<OrderPlacedEvent>` do MassTransit. A cada pedido recebido ele:

1. registra um log com `OrderId`, `UserId`, `GameId` e `Price`;
2. aguarda um delay fixo de 500 ms (simulação de processamento);
3. chama `PaymentDecision.Decide(...)` para obter o status;
4. registra o log da decisão (incluindo o limite vigente);
5. publica `PaymentProcessedEvent` com o resultado.

### `PaymentDecision`

Em `src/Fcg.Payments.Api/Payments/PaymentDecision.cs`. Contém a **lógica de negócio isolada e testável** — uma classe estática pura, sem dependências de infraestrutura:

```csharp
public static string Decide(decimal price, decimal maxApprovedAmount)
    => price <= maxApprovedAmount ? Approved : Rejected;
```

Por ser uma função pura, ela é facilmente testada de forma unitária (ver seção [Testes](#11-testes)), enquanto o `OrderPlacedConsumer` cuida apenas da orquestração (mensageria, log e delay).

---

## 4. Pré-requisitos

- **.NET 10 SDK** (versão fixada em `global.json`: `10.0.100`).
- **Docker** — para subir o RabbitMQ localmente e/ou para empacotar o serviço.
- Um **RabbitMQ** acessível **com o plugin `rabbitmq_delayed_message_exchange`** (exigido pelo
  delayed redelivery do MassTransit). A imagem `masstransit/rabbitmq` já traz o plugin; a oficial
  `rabbitmq:3-management` **não**. (No compose/k8s da plataforma, o repo `orchestration` usa uma
  imagem custom com o plugin.) Para rodar localmente:

```bash
docker run -d --name rabbitmq \
  -p 5672:5672 -p 15672:15672 \
  masstransit/rabbitmq:3.13.1
```

O painel de administração fica em `http://localhost:15672` (usuário/senha padrão: `guest` / `guest`).

> **Resiliência da mensageria:** o consumer usa retry imediato **exponencial** (3 tentativas) e,
> esgotado, **delayed redelivery** com intervalos crescentes (60/300/900s) antes de a mensagem ir
> para a fila `payments-order-placed-event_error` (dead-letter), sem ser perdida. Os intervalos são
> configuráveis (`RabbitMq__ImmediateRetryCount`, `RabbitMq__DelayedRedeliverySeconds`).

---

## 5. Variáveis de ambiente

Todas as configurações podem ser sobrescritas por variáveis de ambiente usando o separador de **duplo underscore** (`__`) entre seção e chave.

| Variável (`Secao__Chave`)     | Descrição                                                        | Default            |
| ----------------------------- | ---------------------------------------------------------------- | ------------------ |
| `RabbitMq__Host`              | Host do broker RabbitMQ.                                         | `localhost`        |
| `RabbitMq__Username`          | Usuário do RabbitMQ.                                            | `guest`            |
| `RabbitMq__Password`          | Senha do RabbitMQ.                                              | `guest`            |
| `RabbitMq__ImmediateRetryCount`   | Nº de tentativas do retry imediato (exponencial) no consumer.              | `3`                |
| `RabbitMq__DelayedRedeliverySeconds` | Intervalos (s, separados por vírgula) do delayed redelivery.           | `60,300,900`       |
| `MongoDbSettings__ConnectionString` | Connection string do MongoDB (com `?replicaSet=rs0`).                  | `mongodb://localhost:27017/?replicaSet=rs0` |
| `MongoDbSettings__DatabaseName`   | Nome do database de auditoria dos pagamentos.                             | `paymentsdb`       |
| `Payments__MaxApprovedAmount` | Valor máximo aprovado automaticamente; acima disso é rejeitado. | `5000`             |
| `ASPNETCORE_ENVIRONMENT`      | Ambiente da aplicação (`Development` / `Production`).            | `Production`       |

> A porta de escuta dentro do container é definida no `Dockerfile` via `ASPNETCORE_URLS=http://+:8080`.

---

## 6. Como rodar localmente (dotnet)

1. Suba um RabbitMQ (ver [Pré-requisitos](#4-pré-requisitos)):

```bash
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 masstransit/rabbitmq:3.13.1
```

2. Rode o serviço:

```bash
dotnet run --project src/Fcg.Payments.Api
```

A aplicação escuta na porta **8080**. Verifique o health check:

```bash
curl http://localhost:8080/health
```

### Vendo os logs de decisão

O serviço registra duas linhas por pedido processado no console (saída padrão do `dotnet run`):

```
Pedido recebido para processamento de pagamento. OrderId=..., UserId=..., GameId=..., Valor=...
Pagamento processado. OrderId=..., Status=Approved (limite de aprovação: 5000)
```

Como não há API REST de negócio, esses logs são a principal forma de observar o que o serviço está fazendo a cada `OrderPlacedEvent` consumido.

---

## 7. Como rodar com Docker

Build da imagem:

```bash
docker build -t payments-api:local .
```

Execução (mapeando a porta `8083` do host para a `8080` do container e apontando para um RabbitMQ rodando no host):

```bash
docker run --rm -p 8083:8080 \
  -e RabbitMq__Host=host.docker.internal \
  payments-api:local
```

Health check: `curl http://localhost:8083/health`.

---

## 8. Rodar o ecossistema completo (end-to-end)

O PaymentsAPI sozinho não faz nada visível — ele precisa receber `OrderPlacedEvent` do CatalogAPI. Para uma experiência completa (todos os serviços + RabbitMQ juntos), use o repositório de orquestração:

**https://github.com/fcg-grupo-16/orchestration**

```bash
git clone https://github.com/fcg-grupo-16/orchestration
cd orchestration
docker compose up
```

### Observando uma decisão de ponta a ponta

1. **Cadastre um usuário** no UsersAPI.
2. **Inicie uma compra** de um jogo no CatalogAPI — isso publica um `OrderPlacedEvent`.
3. **Observe o log do PaymentsAPI** (`docker compose logs -f payments-api`): você verá as duas linhas de log (`Pedido recebido...` e `Pagamento processado... Status=Approved`).
4. Se aprovado, o CatalogAPI adiciona o jogo à biblioteca do usuário e o NotificationsAPI envia a confirmação.

### Forçando um `Rejected`

Como a decisão depende apenas de `Price` vs. `Payments__MaxApprovedAmount`, há duas formas de provocar uma rejeição:

- **Comprar um jogo cujo preço seja maior que o limite** (por padrão, acima de `5000`); ou
- **Baixar o limite** via variável de ambiente, por exemplo `Payments__MaxApprovedAmount=10`, e iniciar qualquer compra acima desse valor.

---

## 9. Eventos

| Direção      | Evento                  | Origem / Destino                             | Campos                                                                 |
| ------------ | ----------------------- | -------------------------------------------- | ---------------------------------------------------------------------- |
| **Consome**  | `OrderPlacedEvent`      | Publicado pelo CatalogAPI                    | `OrderId` (Guid), `UserId` (string), `GameId` (string), `Price` (decimal) |
| **Publica**  | `PaymentProcessedEvent` | Consumido por CatalogAPI e NotificationsAPI  | `OrderId` (Guid), `UserId` (string), `GameId` (string), `Price` (decimal), `Status` (string: `Approved`/`Rejected`) |

Ambos os contratos vivem em `src/Fcg.Payments.Api/Contracts/Events.cs`, no namespace `Fcg.Contracts.Events`, e precisam ser idênticos em todos os serviços FCG.

---

## 10. Testes

```bash
dotnet test
```

Os testes unitários (`tests/Fcg.Payments.UnitTests/PaymentDecisionTests.cs`) cobrem a regra de decisão `PaymentDecision`, incluindo:

- valores **abaixo** do limite (ex.: `0`, `1`, `2500`, `4999.99`) → `Approved`;
- valor **igual** ao limite (fronteira) → `Approved`;
- valores **acima** do limite (ex.: `5000.01`, `5001`, `10000`) → `Rejected`;
- **determinismo** (mesma entrada produz sempre o mesmo resultado).

---

## 11. Como contribuir

1. Abra (ou assuma) uma **issue** descrevendo a mudança.
2. Crie uma **branch** a partir de `main` no padrão:
   - `feat/<n>-descricao-curta` para novas funcionalidades;
   - `fix/<n>-descricao-curta` para correções.
   (`<n>` é o número da issue.)
3. Faça commits seguindo **Conventional Commits** (`feat:`, `fix:`, `docs:`, `test:`, `chore:`, ...).
4. Abra um **Pull Request** para `main` referenciando a issue com `Closes #n`.
5. Garanta o **CI verde** (build + testes).
6. Após a revisão e aprovação, faça o **merge**.

> **Nunca** faça commit de segredos (credenciais, tokens, senhas). Use variáveis de ambiente / Secrets.

---

## 12. Deploy de versão

O versionamento segue **SemVer** por tag (`vX.Y.Z`).

1. Build da imagem com a tag de versão, apontando para o GitHub Container Registry (GHCR):

```bash
docker build -t ghcr.io/fcg-grupo-16/payments-api:vX.Y.Z .
```

2. Push para o GHCR:

```bash
docker push ghcr.io/fcg-grupo-16/payments-api:vX.Y.Z
```

3. Atualize a imagem no Kubernetes — editando o campo `image` em `k8s/deployment.yaml`:

```yaml
image: ghcr.io/fcg-grupo-16/payments-api:vX.Y.Z
```

   ou diretamente via `kubectl`:

```bash
kubectl set image deployment/payments-api \
  payments-api=ghcr.io/fcg-grupo-16/payments-api:vX.Y.Z -n fcg
```

4. Aplique o manifesto (caso tenha editado o arquivo):

```bash
kubectl apply -f k8s/deployment.yaml -n fcg
```

---

## 13. Kubernetes

Os manifestos ficam em `k8s/`:

| Arquivo            | Recurso     | Função                                                                        |
| ------------------ | ----------- | ----------------------------------------------------------------------------- |
| `configmap.yaml`   | ConfigMap   | Config não sensível (`RabbitMq__Host`, `Payments__MaxApprovedAmount`, `ASPNETCORE_ENVIRONMENT`). |
| `secret.yaml`      | Secret      | Credenciais do RabbitMQ (`RabbitMq__Username`, `RabbitMq__Password`).         |
| `deployment.yaml`  | Deployment  | 1 réplica; porta 8080; `livenessProbe`/`readinessProbe` em `/health`; carrega config via `envFrom`. |
| `service.yaml`     | Service     | `ClusterIP` expondo a porta 80 → `targetPort` 8080.                           |

Aplicação isolada (para testes locais em um cluster como kind/minikube):

```bash
kubectl apply -f k8s/ -n fcg
```

> O deploy agregado de todos os serviços FCG é feito pelo repositório **[orchestration](https://github.com/fcg-grupo-16/orchestration)**.

---

## 14. Troubleshooting

**O serviço sobe mas fica tentando reconectar / erros de conexão com o broker.**
O RabbitMQ provavelmente está indisponível ou o host está errado. Confirme que o broker está rodando (`docker ps`), que `RabbitMq__Host` aponta para o endereço correto (`localhost` em dotnet local; `host.docker.internal` ao rodar o container apontando para o host; `rabbitmq` dentro do compose/cluster) e que usuário/senha conferem.

**O serviço sobe, mas nunca recebe eventos / nenhum log de "Pedido recebido".**
- Verifique se o **CatalogAPI está publicando** `OrderPlacedEvent` (inicie uma compra de fato).
- Confirme que ambos os serviços usam o **mesmo broker** e o mesmo `namespace`/nome de tipo (`Fcg.Contracts.Events.OrderPlacedEvent`) — qualquer divergência muda a URN da mensagem e o exchange, quebrando o roteamento.
- No painel do RabbitMQ (`http://localhost:15672`) verifique se a exchange/fila do payments foi criada e se há mensagens chegando.

**Porta 8080 já em uso (`address already in use`).**
Outro processo está usando a 8080. Pare-o ou, ao rodar via Docker, mapeie outra porta no host (ex.: `-p 8083:8080`). Em dotnet local, ajuste `ASPNETCORE_URLS` (ex.: `ASPNETCORE_URLS=http://+:8081`).
