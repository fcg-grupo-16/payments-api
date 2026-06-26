using Fcg.Payments.Payments;
using FluentAssertions;
using Xunit;

namespace Fcg.Payments.UnitTests;

public class PaymentDecisionTests
{
    private const decimal Limit = 5000m;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2500)]
    [InlineData(4999.99)]
    public void Decide_DeveAprovar_QuandoValorAbaixoDoLimite(decimal price)
    {
        var status = PaymentDecision.Decide(price, Limit);

        status.Should().Be(PaymentDecision.Approved);
    }

    [Fact]
    public void Decide_DeveAprovar_QuandoValorIgualAoLimite()
    {
        var status = PaymentDecision.Decide(Limit, Limit);

        status.Should().Be(PaymentDecision.Approved);
    }

    [Theory]
    [InlineData(5000.01)]
    [InlineData(5001)]
    [InlineData(10000)]
    public void Decide_DeveRejeitar_QuandoValorAcimaDoLimite(decimal price)
    {
        var status = PaymentDecision.Decide(price, Limit);

        status.Should().Be(PaymentDecision.Rejected);
    }

    [Fact]
    public void Decide_DeveSerDeterministico_ParaMesmaEntrada()
    {
        var first = PaymentDecision.Decide(1234.56m, Limit);
        var second = PaymentDecision.Decide(1234.56m, Limit);

        first.Should().Be(second);
    }
}
