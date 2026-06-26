# FCG — payments-api

Microsserviço de **simulação de pagamentos** da plataforma FIAP Cloud Games (FCG), Fase 2.

## Propósito

O `payments-api` consome o evento `OrderPlacedEvent` (publicado pelo CatalogAPI ao
iniciar a compra de um jogo), **simula** o processamento do pagamento e publica o
evento `PaymentProcessedEvent` com `Status` igual a `"Approved"` ou `"Rejected"`.

A comunicação entre os serviços é feita via **RabbitMQ + MassTransit**. Os contratos
de evento estão em `src/Fcg.Payments.Api/Contracts/Events.cs` (namespace
`Fcg.Contracts.Events`), idênticos em todos os serviços FCG.

## Regra de aprovação

A decisão é **determinística** (para tornar o demo reproduzível):

- `Approved` quando `Price <= Payments:MaxApprovedAmount` (padrão `5000`);
- `Rejected` caso contrário.

A lógica vive em `PaymentDecision.Decide(price, maxApprovedAmount)`.

## Variáveis de ambiente

Todas as configurações são sobrescrevíveis por variáveis de ambiente usando o
separador de duplo underscore (`__`).

| Variável                      | Padrão      | Descrição                                            |
| ----------------------------- | ----------- | ---------------------------------------------------- |
| `RabbitMq__Host`              | `localhost` | Host do broker RabbitMQ.                             |
| `RabbitMq__Username`          | `guest`     | Usuário do RabbitMQ.                                 |
| `RabbitMq__Password`          | `guest`     | Senha do RabbitMQ.                                   |
| `Payments__MaxApprovedAmount` | `5000`      | Valor máximo aprovado automaticamente.               |
| `ASPNETCORE_ENVIRONMENT`      | `Production`| Ambiente da aplicação (`Development`/`Production`).  |
| `ASPNETCORE_URLS`             | `http://+:8080` | Endereço de escuta (definido no Dockerfile).     |

## Endpoints

- `GET /health` — health check (liveness/readiness).

O serviço não expõe API REST de negócio; sua única responsabilidade HTTP é o health
check. O trabalho real acontece no consumidor de mensagens.

## Como executar

### Local (`dotnet run`)

Requer um RabbitMQ acessível (ex.: `docker run -p 5672:5672 -p 15672:15672 rabbitmq:3-management`).

```bash
dotnet run --project src/Fcg.Payments.Api
```

A aplicação escuta na porta `8080`.

### Docker

```bash
# build da imagem
docker build -t payments-api:local .

# execução (apontando para um RabbitMQ no host)
docker run --rm -p 8080:8080 \
  -e RabbitMq__Host=host.docker.internal \
  payments-api:local
```

## Testes

```bash
dotnet test -c Release
```

Os testes unitários cobrem a regra de decisão (`PaymentDecision`): valores abaixo,
iguais e acima do limite, além do determinismo.
