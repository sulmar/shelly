using System.ClientModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

namespace Shelly.Evaluation;

// Testy nie przyjmują argumentów wiersza poleceń, dlatego konfigurację czytamy ze zmiennych środowiskowych.
public static class EvaluationSettings
{
    // Model testowanego agenta.
    public static string AgentModel { get; } =
        Environment.GetEnvironmentVariable("SHELLY_MODEL") ?? "qwen3:8b";

    // Model-sędzia oceniający odpowiedzi agenta. Domyślnie ten sam co agent.
    public static string JudgeModel { get; } =
        Environment.GetEnvironmentVariable("SHELLY_JUDGE_MODEL") ?? AgentModel;

    // Opcjonalnie: dowolne API zgodne z OpenAI zamiast lokalnej Ollamy (klucz w LLM_API_KEY).
    public static string? Endpoint { get; } =
        Environment.GetEnvironmentVariable("LLM_ENDPOINT");

    // Wyniki ewaluacji zapisujemy w katalogu projektu, aby łatwo wygenerować z nich raport.
    public static string StoragePath { get; } =
        Path.Combine(ProjectDirectory(), "eval-results");

    public static IChatClient CreateChatClient(string model)
    {
        // Model-sędzia generuje długie uzasadnienia, a lokalna Ollama bywa wolna,
        // dlatego wydłużamy domyślny limit czasu HttpClient (100 s).
        if (Endpoint is null)
            return new OllamaApiClient(
                new HttpClient
                {
                    BaseAddress = new Uri("http://localhost:11434"),
                    Timeout = TimeSpan.FromMinutes(5)
                },
                model);

        var apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY")
                     ?? throw new InvalidOperationException("Set the LLM_API_KEY environment variable to use LLM_ENDPOINT.");

        return new OpenAIClient(
                new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(Endpoint) })
            .GetChatClient(model)
            .AsIChatClient();
    }

    private static string ProjectDirectory([CallerFilePath] string path = "") =>
        Path.GetDirectoryName(path)!;
}
