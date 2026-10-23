using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Xunit.Abstractions;

namespace Shelly.Evaluation;

public class ShellyAgentEvaluationTests(ITestOutputHelper output)
{
    // Wszystkie scenariusze jednego uruchomienia testów trafiają do raportu pod wspólną nazwą.
    private static readonly string ExecutionName = DateTime.Now.ToString("yyyyMMddTHHmmss");

    private static readonly ReportingConfiguration Reporting =
        DiskBasedReportingConfiguration.Create(
            storageRootPath: EvaluationSettings.StoragePath,
            evaluators:
            [
                // Deterministyczny: czy wywołano właściwe narzędzia.
                new ExpectedToolCallsEvaluator(),

                // LLM-as-a-judge: model-sędzia ocenia działanie agenta.
                new ToolCallAccuracyEvaluator(),
                new TaskAdherenceEvaluator(),
                new IntentResolutionEvaluator()
            ],
            chatConfiguration: new ChatConfiguration(
                EvaluationSettings.CreateChatClient(EvaluationSettings.JudgeModel)),
            // Odpowiedzi sędziego są zapamiętywane. Ta sama odpowiedź agenta nie jest oceniana ponownie.
            enableResponseCaching: true,
            executionName: ExecutionName);

    [Theory]
    [InlineData("TurnOn", false, "Włącz lampkę.", true, "turn_on_light", "turn_off_light")]
    [InlineData("TurnOff", true, "Wyłącz lampkę.", false, "turn_off_light", "turn_on_light")]
    [InlineData("Status", false, "Jaki jest stan lampki?", false, "get_light_status", "turn_on_light,turn_off_light")]
    [InlineData("EnsureOn_AlreadyOn", true, "Upewnij się, że lampka jest włączona.", true, "get_light_status", "turn_on_light,turn_off_light")]
    [InlineData("EnsureOn_WhenOff", false, "Upewnij się, że lampka jest włączona.", true, "get_light_status,turn_on_light", "turn_off_light")]
    public async Task Agent_completes_task(
        string scenario,
        bool initiallyOn,
        string input,
        bool expectedOn,
        string expectedTools,
        string forbiddenTools)
    {
        // Arrange
        var device = new FakeShellyDevice { Output = initiallyOn };
        using var http = device.CreateClient();

        var agent = new ShellyAgent(
            EvaluationSettings.CreateChatClient(EvaluationSettings.AgentModel),
            http);

        // Act
        var (messages, response) = await agent.RunAsync(input);

        output.WriteLine($"User: {input}");
        output.WriteLine($"Assistant: {response.Text}");

        // Evaluate
        // Wynik scenariusza zostaje zapisany na dysku przy zwolnieniu ScenarioRun, także gdy asercje nie przejdą.
        await using var scenarioRun = await Reporting.CreateScenarioRunAsync(
            $"Shelly.Agent.{scenario}");

        var result = await scenarioRun.EvaluateAsync(
            messages,
            response,
            additionalContext:
            [
                new ExpectedToolCallsContext(Split(expectedTools), Split(forbiddenTools)),
                new ToolCallAccuracyEvaluatorContext(agent.Tools),
                new TaskAdherenceEvaluatorContext(agent.Tools),
                new IntentResolutionEvaluatorContext(agent.Tools)
            ]);

        foreach (var metric in result.Metrics.Values)
        {
            output.WriteLine(
                $"[{metric.Name}] {FormatValue(metric)} " +
                $"{(metric.Interpretation?.Failed == true ? "FAILED" : "")} {metric.Reason}");
        }

        // Assert
        // 1. Stan świata: najważniejsze kryterium, niezależne od tego, co agent odpowiedział.
        Assert.Equal(expectedOn, device.Output);

        // 2. Przebieg: właściwe narzędzia (deterministycznie).
        var toolCalls = result.Get<BooleanMetric>(ExpectedToolCallsEvaluator.MetricName);
        Assert.True(toolCalls.Value, toolCalls.Reason);

        // Oceny modelu-sędziego (ToolCallAccuracy, TaskAdherence, IntentResolution) trafiają do raportu,
        // ale nie przerywają testu: sędzia też jest modelem językowym i może się mylić.
    }

    private static string[] Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string FormatValue(EvaluationMetric metric) => metric switch
    {
        BooleanMetric boolean => boolean.Value?.ToString() ?? "-",
        NumericMetric numeric => numeric.Value?.ToString("0.#") ?? "-",
        _ => "-"
    };
}
