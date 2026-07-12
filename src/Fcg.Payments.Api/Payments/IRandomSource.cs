namespace Fcg.Payments.Payments;

/// <summary>Fonte de aleatoriedade injetável (testável) — evita `new Random()` solto nas regras.</summary>
public interface IRandomSource
{
    /// <summary>Retorna um double em [0, 1).</summary>
    double NextDouble();
}

/// <summary>Implementação padrão baseada em <see cref="Random.Shared"/> (thread-safe).</summary>
public sealed class RandomSource : IRandomSource
{
    public double NextDouble() => Random.Shared.NextDouble();
}
