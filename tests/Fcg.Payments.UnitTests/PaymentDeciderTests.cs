using Fcg.Payments.Payments;
using Fcg.Payments.Payments.Rules;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fcg.Payments.UnitTests;

public class PaymentDeciderTests
{
    private const decimal Limit = 5000m;

    // Fonte de aleatoriedade determinística para teste (percorre a sequência informada).
    // Respeita o contrato de IRandomSource: valores em [0, 1) — usa BitDecrement(1.0) como "quase 1".
    private static readonly double AlmostOne = double.BitDecrement(1.0);

    private sealed class SequenceRandom(params double[] values) : IRandomSource
    {
        private int _i;
        public double NextDouble() => values.Length == 0 ? AlmostOne : values[_i++ % values.Length];
    }

    private static IPaymentDecider BuildDecider(PaymentsOptions opt, IRandomSource? random = null)
    {
        var options = Options.Create(opt);
        // Sem random informado: ~1 nunca é < rate -> a regra aleatória nunca rejeita.
        var rnd = random ?? new SequenceRandom(AlmostOne);
        var rules = new IPaymentRule[]
        {
            new AmountLimitRule(options),
            new BlockedUserRule(options),
            new BlockedGameRule(options),
            new RandomFailureRule(options, rnd),
        };
        return new PaymentDecider(rules);
    }

    private static PaymentContext Order(decimal price, string userId = "u", string gameId = "g")
        => new(userId, gameId, price);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2500)]
    [InlineData(4999.99)]
    [InlineData(5000)] // igual ao limite: aprova
    public void Decide_DeveAprovar_QuandoValorAteOLimite_ESemOutrasRejeicoes(decimal price)
    {
        var decider = BuildDecider(new PaymentsOptions { MaxApprovedAmount = Limit });

        decider.Decide(Order(price)).Status.Should().Be(PaymentDecision.Approved);
    }

    [Theory]
    [InlineData(5000.01)]
    [InlineData(5001)]
    [InlineData(10000)]
    public void Decide_DeveRejeitar_QuandoValorAcimaDoLimite(decimal price)
    {
        var decider = BuildDecider(new PaymentsOptions { MaxApprovedAmount = Limit });

        decider.Decide(Order(price)).Status.Should().Be(PaymentDecision.Rejected);
    }

    [Fact]
    public void Decide_DeveRejeitar_QuandoUsuarioBloqueado_MesmoComValorOk()
    {
        var decider = BuildDecider(new PaymentsOptions
        {
            MaxApprovedAmount = Limit,
            BlockedUserIds = ["banido"]
        });

        decider.Decide(Order(100m, userId: "banido")).Status.Should().Be(PaymentDecision.Rejected);
        decider.Decide(Order(100m, userId: "ok")).Status.Should().Be(PaymentDecision.Approved);
    }

    [Fact]
    public void Decide_DeveRejeitar_QuandoJogoBloqueado_MesmoComValorOk()
    {
        var decider = BuildDecider(new PaymentsOptions
        {
            MaxApprovedAmount = Limit,
            BlockedGameIds = ["jogo-proibido"]
        });

        decider.Decide(Order(100m, gameId: "jogo-proibido")).Status.Should().Be(PaymentDecision.Rejected);
        decider.Decide(Order(100m, gameId: "outro")).Status.Should().Be(PaymentDecision.Approved);
    }

    [Fact]
    public void Decide_ComTaxaZero_DeveSerDeterministico()
    {
        var decider = BuildDecider(new PaymentsOptions { MaxApprovedAmount = Limit, RandomFailureRate = 0 });

        var a = decider.Decide(Order(100m));
        var b = decider.Decide(Order(100m));
        a.Status.Should().Be(PaymentDecision.Approved);
        a.Should().Be(b);
    }

    [Fact]
    public void Decide_ComTaxaAleatoria_DeveRejeitarNaProporcaoEsperada()
    {
        // rate 0.5; sequência [0.1, 0.9, 0.3, 0.7] -> rejeita quando NextDouble() < 0.5 (0.1 e 0.3).
        var random = new SequenceRandom(0.1, 0.9, 0.3, 0.7);
        var decider = BuildDecider(
            new PaymentsOptions { MaxApprovedAmount = Limit, RandomFailureRate = 0.5 }, random);

        var resultados = Enumerable.Range(0, 4).Select(_ => decider.Decide(Order(100m))).ToList();

        resultados.Count(r => r.Status == PaymentDecision.Rejected).Should().Be(2);
        resultados.Count(r => r.Status == PaymentDecision.Approved).Should().Be(2);
    }

    [Fact]
    public void Decide_DeveNomearARegra_QuandoRejeitaPorValor()
    {
        var decider = BuildDecider(new PaymentsOptions { MaxApprovedAmount = Limit });

        var outcome = decider.Decide(Order(9999m));

        outcome.Status.Should().Be(PaymentDecision.Rejected);
        outcome.Rule.Should().Be("AmountLimit");
    }

    [Fact]
    public void Decide_DeveNomearARegra_QuandoRejeitaPorUsuarioBloqueado()
    {
        var decider = BuildDecider(new PaymentsOptions
        {
            MaxApprovedAmount = Limit,
            BlockedUserIds = ["banido"]
        });

        decider.Decide(Order(100m, userId: "banido")).Rule.Should().Be("BlockedUser");
    }

    [Fact]
    public void Decide_DeveNomearARegra_QuandoRejeitaPorJogoBloqueado()
    {
        var decider = BuildDecider(new PaymentsOptions
        {
            MaxApprovedAmount = Limit,
            BlockedGameIds = ["jogo-proibido"]
        });

        decider.Decide(Order(100m, gameId: "jogo-proibido")).Rule.Should().Be("BlockedGame");
    }

    [Fact]
    public void Decide_Aprovado_DeveUsarSemRegra()
    {
        var decider = BuildDecider(new PaymentsOptions { MaxApprovedAmount = Limit });

        var outcome = decider.Decide(Order(100m));

        outcome.Status.Should().Be(PaymentDecision.Approved);
        outcome.Rule.Should().Be(PaymentOutcome.SemRegra);
    }

    [Fact]
    public void Decide_ComDuasRegrasRejeitando_DeveCreditarAPRIMEIRA()
    {
        // Documenta a semântica do FirstOrDefault: a ordem de registro decide o label `rule`.
        // Valor acima do limite E usuário bloqueado -> AmountLimit vem primeiro na lista.
        var decider = BuildDecider(new PaymentsOptions
        {
            MaxApprovedAmount = Limit,
            BlockedUserIds = ["banido"]
        });

        decider.Decide(Order(9999m, userId: "banido")).Rule.Should().Be("AmountLimit");
    }
}
