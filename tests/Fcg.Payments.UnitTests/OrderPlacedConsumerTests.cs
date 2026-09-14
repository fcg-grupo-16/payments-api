using System.Diagnostics.Metrics;
using Fcg.Contracts.Events;
using Fcg.Payments.Consumers;
using Fcg.Payments.Observability;
using Fcg.Payments.Payments;
using Fcg.Payments.Payments.Rules;
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

    private static ServiceProvider BuildHarness(FakePaymentRepository repo, PaymentsOptions? options = null) =>
        new ServiceCollection()
            .AddSingleton<IPaymentRepository>(repo)
            .AddSingleton(Options.Create(options ?? new PaymentsOptions { MaxApprovedAmount = 5000m }))
            .AddSingleton<IRandomSource, RandomSource>()
            .AddSingleton<IPaymentRule, AmountLimitRule>()
            .AddSingleton<IPaymentRule, BlockedUserRule>()
            .AddSingleton<IPaymentRule, BlockedGameRule>()
            .AddSingleton<IPaymentRule, RandomFailureRule>()
            .AddSingleton<IPaymentDecider, PaymentDecider>()
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

    [Fact]
    public async Task Consume_DeveRejeitar_QuandoUsuarioBloqueado_MesmoComValorOk()
    {
        // Valida que as regras de decisão (#4) propagam pelo consumer até o evento publicado.
        var repo = new FakePaymentRepository();
        await using var provider = BuildHarness(repo,
            new PaymentsOptions { MaxApprovedAmount = 5000m, BlockedUserIds = ["banido"] });
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var orderId = Guid.NewGuid();
            await harness.Bus.Publish(new OrderPlacedEvent
            {
                OrderId = orderId, UserId = "banido", GameId = "g", Price = 100m // valor OK, mas usuário bloqueado
            });

            (await harness.Consumed.Any<OrderPlacedEvent>()).Should().BeTrue();
            (await harness.Published.Any<PaymentProcessedEvent>(x =>
                x.Context.Message.OrderId == orderId && x.Context.Message.Status == PaymentDecision.Rejected))
                .Should().BeTrue();
        }
        finally
        {
            await harness.Stop();
        }
    }

    // Coleta as medições de fcg_payment_decisions_total durante um teste.
    //
    // ⚠️ O PaymentMetrics usa um Meter ESTÁTICO, então um listener é global ao processo. Isso é
    // seguro aqui porque o xUnit roda os testes de uma MESMA classe em sequência, e esta é a única
    // classe que aciona o consumer (o PaymentDeciderTests chama o decider direto, que não emite
    // métrica). Mover este teste para outra classe reintroduziria interferência entre testes
    // paralelos — e, como o label `rule` é deliberadamente de baixa cardinalidade (sem OrderId),
    // não haveria como atribuir uma medição ao teste que a gerou.
    private sealed class ColetorDeDecisoes : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Status, string Rule)> _medicoes = [];
        private readonly Lock _gate = new();

        public ColetorDeDecisoes()
        {
            _listener.InstrumentPublished = (instrumento, ouvinte) =>
            {
                if (instrumento.Meter.Name == PaymentMetrics.MeterName
                    && instrumento.Name == "fcg_payment_decisions_total")
                {
                    ouvinte.EnableMeasurementEvents(instrumento);
                }
            };

            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                string status = "", rule = "";
                foreach (var tag in tags)
                {
                    if (tag.Key == "status") { status = tag.Value?.ToString() ?? ""; }
                    else if (tag.Key == "rule") { rule = tag.Value?.ToString() ?? ""; }
                }

                lock (_gate) { _medicoes.Add((status, rule)); }
            });

            _listener.Start();
        }

        public IReadOnlyList<(string Status, string Rule)> Medicoes
        {
            get { lock (_gate) { return _medicoes.ToList(); } }
        }

        public void Dispose() => _listener.Dispose();
    }

    [Fact]
    public async Task Consume_Duplicata_NaoDeveContarComoDecisaoNova()
    {
        // A afirmação mais sutil desta instrumentação: a duplicata NÃO é uma decisão. Se fosse
        // contada, o mesmo pagamento entraria duas vezes no denominador e a taxa de aprovação do
        // dashboard ficaria errada. O código garante isso pela POSIÇÃO da chamada (depois do
        // early-return do `!inserido`) — e posição é exatamente o que um refactor futuro quebra
        // sem que nada acuse. Daí este teste.
        using var coletor = new ColetorDeDecisoes();
        var repo = new FakePaymentRepository();
        await using var provider = BuildHarness(repo);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var evt = new OrderPlacedEvent
            {
                OrderId = Guid.NewGuid(), UserId = "u", GameId = "g", Price = 100m
            };

            await harness.Bus.Publish(evt);
            await harness.Bus.Publish(evt);

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

            var medicoes = coletor.Medicoes;

            // Exatamente UMA decisão real, aprovada e sem regra acionada.
            medicoes.Count(m => m.Status == PaymentDecision.Approved).Should().Be(1);
            medicoes.Single(m => m.Status == PaymentDecision.Approved)
                .Rule.Should().Be(PaymentOutcome.SemRegra);

            // ...e exatamente UMA duplicata, numa série SEPARADA — que as queries de taxa excluem.
            medicoes.Count(m => m.Status == PaymentMetrics.StatusDuplicate).Should().Be(1);
            medicoes.Single(m => m.Status == PaymentMetrics.StatusDuplicate)
                .Rule.Should().Be(PaymentMetrics.RuleIdempotency);

            // Nenhuma rejeição: a duplicata não pode ter virado Rejected por acidente.
            medicoes.Should().NotContain(m => m.Status == PaymentDecision.Rejected);
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task Consume_Rejeitado_DeveContarAMetricaComONomeDaRegra()
    {
        using var coletor = new ColetorDeDecisoes();
        var repo = new FakePaymentRepository();
        await using var provider = BuildHarness(repo,
            new PaymentsOptions { MaxApprovedAmount = 5000m, BlockedUserIds = ["banido"] });
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new OrderPlacedEvent
            {
                OrderId = Guid.NewGuid(), UserId = "banido", GameId = "g", Price = 100m
            });

            (await harness.Consumed.Any<OrderPlacedEvent>()).Should().BeTrue();

            coletor.Medicoes.Should().ContainSingle()
                .Which.Should().Be((PaymentDecision.Rejected, "BlockedUser"));
        }
        finally
        {
            await harness.Stop();
        }
    }
}
