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
    private sealed class SequenceRandom(params double[] values) : IRandomSource
    {
        private int _i;
        public double NextDouble() => values.Length == 0 ? 1.0 : values[_i++ % values.Length];
    }

    private static IPaymentDecider BuildDecider(PaymentsOptions opt, IRandomSource? random = null)
    {
        var options = Options.Create(opt);
        // Sem random informado: 1.0 nunca é < rate -> a regra aleatória nunca rejeita.
        var rnd = random ?? new SequenceRandom(1.0);
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

        decider.Decide(Order(price)).Should().Be(PaymentDecision.Approved);
    }

    [Theory]
    [InlineData(5000.01)]
    [InlineData(5001)]
    [InlineData(10000)]
    public void Decide_DeveRejeitar_QuandoValorAcimaDoLimite(decimal price)
    {
        var decider = BuildDecider(new PaymentsOptions { MaxApprovedAmount = Limit });

        decider.Decide(Order(price)).Should().Be(PaymentDecision.Rejected);
    }

    [Fact]
    public void Decide_DeveRejeitar_QuandoUsuarioBloqueado_MesmoComValorOk()
    {
        var decider = BuildDecider(new PaymentsOptions
        {
            MaxApprovedAmount = Limit,
            BlockedUserIds = ["banido"]
        });

        decider.Decide(Order(100m, userId: "banido")).Should().Be(PaymentDecision.Rejected);
        decider.Decide(Order(100m, userId: "ok")).Should().Be(PaymentDecision.Approved);
    }

    [Fact]
    public void Decide_DeveRejeitar_QuandoJogoBloqueado_MesmoComValorOk()
    {
        var decider = BuildDecider(new PaymentsOptions
        {
            MaxApprovedAmount = Limit,
            BlockedGameIds = ["jogo-proibido"]
        });

        decider.Decide(Order(100m, gameId: "jogo-proibido")).Should().Be(PaymentDecision.Rejected);
        decider.Decide(Order(100m, gameId: "outro")).Should().Be(PaymentDecision.Approved);
    }

    [Fact]
    public void Decide_ComTaxaZero_DeveSerDeterministico()
    {
        var decider = BuildDecider(new PaymentsOptions { MaxApprovedAmount = Limit, RandomFailureRate = 0 });

        var a = decider.Decide(Order(100m));
        var b = decider.Decide(Order(100m));
        a.Should().Be(PaymentDecision.Approved).And.Be(b);
    }

    [Fact]
    public void Decide_ComTaxaAleatoria_DeveRejeitarNaProporcaoEsperada()
    {
        // rate 0.5; sequência [0.1, 0.9, 0.3, 0.7] -> rejeita quando NextDouble() < 0.5 (0.1 e 0.3).
        var random = new SequenceRandom(0.1, 0.9, 0.3, 0.7);
        var decider = BuildDecider(
            new PaymentsOptions { MaxApprovedAmount = Limit, RandomFailureRate = 0.5 }, random);

        var resultados = Enumerable.Range(0, 4).Select(_ => decider.Decide(Order(100m))).ToList();

        resultados.Count(r => r == PaymentDecision.Rejected).Should().Be(2);
        resultados.Count(r => r == PaymentDecision.Approved).Should().Be(2);
    }
}
