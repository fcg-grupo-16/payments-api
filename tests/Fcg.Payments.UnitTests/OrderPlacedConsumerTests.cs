using Fcg.Contracts.Events;
using Fcg.Payments.Consumers;
using Fcg.Payments.Payments;
using Fcg.Payments.Persistence;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fcg.Payments.UnitTests;

public class OrderPlacedConsumerTests
{
    // Fake em memória do repositório — modela o índice único em OrderId de forma ATÔMICA (lock),
    // como o InsertOne do Mongo: sob concorrência, apenas uma inserção vence; a duplicata retorna false.
    private sealed class FakePaymentRepository : IPaymentRepository
    {
        private readonly Lock _gate = new();
        public List<Payment> Registrados { get; } = [];

        public Task<bool> RegistrarAsync(Payment payment, CancellationToken ct = default)
        {
            lock (_gate)
            {
                if (Registrados.Any(p => p.OrderId == payment.OrderId))
                {
                    return Task.FromResult(false); // OrderId já processado
                }

                Registrados.Add(payment);
                return Task.FromResult(true);
            }
        }

        public Task GarantirIndicesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static ServiceProvider BuildHarness(FakePaymentRepository repo) =>
        new ServiceCollection()
            .AddSingleton<IPaymentRepository>(repo)
            .AddSingleton(Options.Create(new PaymentsOptions { MaxApprovedAmount = 5000m }))
            .AddMassTransitTestHarness(x => x.AddConsumer<OrderPlacedConsumer>())
            .BuildServiceProvider(true);

    [Fact]
    public async Task Consume_DevePersistirPagamento_EPublicarProcessed_QuandoAprovado()
    {
        var repo = new FakePaymentRepository();
        await using var provider = BuildHarness(repo);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var orderId = Guid.NewGuid();
            await harness.Bus.Publish(new OrderPlacedEvent
            {
                OrderId = orderId, UserId = "user-1", GameId = "game-1", Price = 100m
            });

            (await harness.Consumed.Any<OrderPlacedEvent>()).Should().BeTrue();

            // Persistiu exatamente um registro, com os campos corretos e Status Approved.
            repo.Registrados.Should().ContainSingle();
            var p = repo.Registrados[0];
            p.OrderId.Should().Be(orderId.ToString());
            p.UserId.Should().Be("user-1");
            p.GameId.Should().Be("game-1");
            p.Price.Should().Be(100m);
            p.Status.Should().Be(PaymentDecision.Approved);
            p.ProcessedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

            // Publicou o PaymentProcessedEvent correspondente.
            (await harness.Published.Any<PaymentProcessedEvent>(x =>
                x.Context.Message.OrderId == orderId && x.Context.Message.Status == PaymentDecision.Approved))
                .Should().BeTrue();
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task Consume_DevePersistirComStatusRejected_QuandoAcimaDoLimite()
    {
        var repo = new FakePaymentRepository();
        await using var provider = BuildHarness(repo);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var orderId = Guid.NewGuid();
            await harness.Bus.Publish(new OrderPlacedEvent
            {
                OrderId = orderId, UserId = "u", GameId = "g", Price = 9999m
            });

            (await harness.Consumed.Any<OrderPlacedEvent>()).Should().BeTrue();
            repo.Registrados.Should().ContainSingle()
                .Which.Status.Should().Be(PaymentDecision.Rejected);

            // Também publica o PaymentProcessedEvent, com o OrderId consumido e Status Rejected.
            (await harness.Published.Any<PaymentProcessedEvent>(x =>
                x.Context.Message.OrderId == orderId && x.Context.Message.Status == PaymentDecision.Rejected))
                .Should().BeTrue();
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task Consume_MesmoOrderIdDuasVezes_DevePersistirEPublicarUmaVezSo()
    {
        var repo = new FakePaymentRepository();
        await using var provider = BuildHarness(repo);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var orderId = Guid.NewGuid();
            var evt = new OrderPlacedEvent { OrderId = orderId, UserId = "u", GameId = "g", Price = 100m };

            // Duas entregas do mesmo OrderId (MessageIds distintos): ambas são consumidas.
            await harness.Bus.Publish(evt);
            await harness.Bus.Publish(evt);

            // Aguarda os DOIS consumos antes de assertar (sem Take, para evitar ambiguidade), com
            // timeout para não pendurar a suíte caso algo consuma menos que o esperado.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var consumidos = 0;
            await foreach (var _ in harness.Consumed.SelectAsync<OrderPlacedEvent>(cts.Token))
            {
                if (++consumidos == 2)
                {
                    break;
                }
            }
            consumidos.Should().Be(2);

            // Idempotência: apenas UM registro persistido.
            repo.Registrados.Where(p => p.OrderId == orderId.ToString()).Should().ContainSingle();

            // ...e apenas UM PaymentProcessedEvent publicado (espera o publish assentar, depois conta).
            (await harness.Published.Any<PaymentProcessedEvent>(x => x.Context.Message.OrderId == orderId))
                .Should().BeTrue();
            harness.Published.Select<PaymentProcessedEvent>(x => x.Context.Message.OrderId == orderId)
                .Should().ContainSingle();
        }
        finally
        {
            await harness.Stop();
        }
    }
}
