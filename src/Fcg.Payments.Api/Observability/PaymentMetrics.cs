using System.Diagnostics.Metrics;

namespace Fcg.Payments.Observability;

/// <summary>
/// Métricas de negócio do processamento de pagamento.
/// </summary>
/// <remarks>
/// <para>
/// Aqui uma métrica CUSTOM se justifica, ao contrário de <c>users-api</c> e <c>catalog-api</c>,
/// onde as métricas HTTP automáticas do ASP.NET Core bastam: este serviço não tem controller — é um
/// worker que consome do RabbitMQ —, então nenhuma métrica automática descreve o que ele faz.
/// "Quantos pagamentos aprovados/rejeitados, e por qual regra" só existe se a emitirmos.
/// </para>
/// <para>
/// <b>Cardinalidade.</b> Os labels são <c>status</c> (3 valores) e <c>rule</c> (número FIXO de
/// regras) — baixa e limitada. NUNCA acrescente <c>UserId</c>, <c>OrderId</c> ou <c>GameId</c> como
/// label: cada valor novo cria uma série temporal no Prometheus, e ids livres explodem a memória do
/// servidor. Ids pertencem a LOGS e TRACES, e é lá que eles estão (ver o consumer).
/// </para>
/// </remarks>
public static class PaymentMetrics
{
    /// <summary>
    /// Nome do meter. Tem de ser registrado com <c>.AddMeter(...)</c> no OpenTelemetry.
    /// </summary>
    /// <remarks>
    /// ⚠️ Esquecer o <c>.AddMeter</c> é a pegadinha clássica: o código compila, o counter
    /// incrementa, e a métrica simplesmente NÃO aparece em <c>/metrics</c>, sem nenhum erro. Se a
    /// métrica sumir, confira essa linha primeiro (está em <c>ObservabilityExtensions</c>).
    /// </remarks>
    public const string MeterName = "Fcg.Payments";

    /// <summary>Label <c>status</c> de uma mensagem duplicada — NÃO é uma decisão nova.</summary>
    public const string StatusDuplicate = "Duplicate";

    /// <summary>Label <c>rule</c> das duplicatas: quem "decidiu" foi a idempotência.</summary>
    public const string RuleIdempotency = "Idempotency";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Decisions = Meter.CreateCounter<long>(
        "fcg_payment_decisions_total",
        unit: "{decision}",
        description: "Decisões de pagamento, por status e pela regra que decidiu.");

    /// <summary>
    /// Limites EXPLÍCITOS do histograma, em segundos.
    /// </summary>
    /// <remarks>
    /// ⚠️ Sem isto o OTel usa os limites padrão — <c>0, 5, 10, 25, 50, 75, 100, 250, 500, 750,
    /// 1000, 2500, 5000, 7500, 10000</c> —, pensados para milissegundos. Como esta métrica está em
    /// SEGUNDOS e o processamento leva ~0,5 s, TODAS as observações caíam no bucket <c>le=5</c>:
    /// medido no cluster, um pagamento de 0,559 s. O histograma não distinguia 0,1 s de 4,9 s, e
    /// <c>histogram_quantile</c> devolveria ~5 s para qualquer p95 — uma métrica que parece certa e
    /// não responde à pergunta que existe para responder.
    /// <para>
    /// Os limites abaixo cercam o valor esperado (o consumer tem um <c>Task.Delay</c> fixo de
    /// 500 ms) com resolução de ambos os lados, e ainda cobrem a cauda até 10 s.
    /// </para>
    /// </remarks>
    private static readonly InstrumentAdvice<double> DuracaoAdvice = new()
    {
        HistogramBucketBoundaries = [0.05, 0.1, 0.25, 0.5, 0.6, 0.75, 1, 2.5, 5, 10]
    };

    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "fcg_payment_processing_duration_seconds",
        unit: "s",
        description: "Tempo de processamento de um OrderPlacedEvent, do consume à publicação.",
        tags: null,
        advice: DuracaoAdvice);

    /// <param name="status">"Approved", "Rejected" ou "Duplicate".</param>
    /// <param name="rule">Nome da regra que decidiu; "None" quando aprovado; "Idempotency" em duplicata.</param>
    public static void RecordDecision(string status, string rule) =>
        Decisions.Add(1,
            new KeyValuePair<string, object?>("status", status),
            new KeyValuePair<string, object?>("rule", rule));

    public static void RecordDuration(double seconds) => Duration.Record(seconds);
}
