using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace Shelly.Evaluation;

// Własny, deterministyczny ewaluator: sprawdza, czy agent wywołał oczekiwane narzędzia
// i nie wywołał zabronionych. Nie potrzebuje modelu-sędziego.
public sealed class ExpectedToolCallsEvaluator : IEvaluator
{
    public const string MetricName = "Expected Tool Calls";

    public IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    public ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var metric = new BooleanMetric(MetricName);
        var result = new EvaluationResult(metric);

        var context = additionalContext?.OfType<ExpectedToolCallsContext>().FirstOrDefault();

        if (context is null)
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Error(
                $"{nameof(ExpectedToolCallsContext)} was not provided."));

            return ValueTask.FromResult(result);
        }

        var called = modelResponse.Messages
            .SelectMany(message => message.Contents)
            .OfType<FunctionCallContent>()
            .Select(call => call.Name)
            .ToList();

        var missing = context.Expected.Except(called).ToList();
        var forbidden = context.Forbidden.Intersect(called).ToList();

        metric.Value = missing.Count == 0 && forbidden.Count == 0;
        metric.Reason =
            $"Wywołane: [{string.Join(", ", called)}]. " +
            $"Brakujące: [{string.Join(", ", missing)}]. " +
            $"Zabronione: [{string.Join(", ", forbidden)}].";

        metric.Interpretation = new EvaluationMetricInterpretation(
            metric.Value == true ? EvaluationRating.Exceptional : EvaluationRating.Unacceptable,
            failed: metric.Value != true,
            reason: metric.Reason);

        return ValueTask.FromResult(result);
    }
}

// Oczekiwania dla konkretnego scenariusza. Treść kontekstu trafia również do raportu.
public sealed class ExpectedToolCallsContext(
    IReadOnlyList<string> expected,
    IReadOnlyList<string> forbidden)
    : EvaluationContext(
        name: "Expected Tool Calls",
        content: $"Oczekiwane: [{string.Join(", ", expected)}]. Zabronione: [{string.Join(", ", forbidden)}].")
{
    public IReadOnlyList<string> Expected { get; } = expected;

    public IReadOnlyList<string> Forbidden { get; } = forbidden;
}
