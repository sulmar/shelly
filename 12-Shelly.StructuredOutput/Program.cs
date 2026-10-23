using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;
using System.ClientModel;

const string DefaultModel = "qwen2.5:3b";
const int MaxDelaySeconds = 3600;

string? shellyUrl = null;
string? endpoint = null;
var model = DefaultModel;

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--model" && i + 1 < args.Length)
        model = args[++i];
    else if (args[i].StartsWith("--model="))
        model = args[i]["--model=".Length..];
    else if (args[i] == "--endpoint" && i + 1 < args.Length)
        endpoint = args[++i];
    else if (args[i].StartsWith("--endpoint="))
        endpoint = args[i]["--endpoint=".Length..];
    else
        shellyUrl ??= args[i];
}

if (shellyUrl is null)
{
    Console.WriteLine($"Usage: Shelly.StructuredOutput <url> [--model <name>] [--endpoint <url>] (default model: {DefaultModel})");
    return;
}

// Klucz API dla zdalnego endpointu czytamy ze zmiennej środowiskowej, aby nie trafił do historii poleceń.
var apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY");

if (endpoint is not null && string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("Set the LLM_API_KEY environment variable to use --endpoint.");
    return;
}

Console.WriteLine($"Connecting to Shelly at {shellyUrl}");
Console.WriteLine($"Using model {model}" + (endpoint is null ? "" : $" at {endpoint}"));

using var http = new HttpClient
{
    BaseAddress = new Uri(shellyUrl)
};

// LLM
// Bez --endpoint używamy lokalnej Ollamy, w przeciwnym razie dowolnego API zgodnego z OpenAI (np. https://llm.hpc.psnc.pl/v1).
// Uwaga: nie ma tu UseFunctionInvocation() ani narzędzi. Model niczego nie wywołuje.
IChatClient client = endpoint is null
    ? new OllamaApiClient(
        new Uri("http://localhost:11434"),
        model)
    : new OpenAIClient(
        new ApiKeyCredential(apiKey!),
        new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
        .GetChatClient(model)
        .AsIChatClient();

// Model ma tylko jedno zadanie: przetłumaczyć polecenie na obiekt LightCommand.
// JSON Schema zawiera tylko nazwy wartości enuma, dlatego ich znaczenie opisujemy w prompcie.
var systemPrompt = new ChatMessage(ChatRole.System, """
    Zamieniasz polecenia użytkownika dotyczące lampki na strukturę danych.

    Znaczenie pola action:
    - TurnOn: użytkownik chce włączyć lampkę lub mieć światło (np. „zapal”, „zrób jasno”).
    - TurnOff: użytkownik chce wyłączyć lampkę lub nie mieć światła (np. „zgaś”, „zrób ciemno”).
    - Status: użytkownik pyta o stan lampki lub jej pobór mocy.
    - Unknown: polecenie nie dotyczy lampki albo jest niejasne.

    Jeśli użytkownik nie podał opóźnienia, ustaw delaySeconds na 0.
    Opóźnienie podane w minutach lub godzinach przelicz na sekundy.
    """);

// Do wyświetlania wyniku modelu: te same ustawienia co MEAI (enumy jako tekst, camelCase), ale w jednej linii.
var jsonOptions = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions) { WriteIndented = false };

Console.WriteLine("Enter a command (or 'exit' to quit):");

while (true)
{
    Console.Write("> ");

    var input = Console.ReadLine();

    if (input is null || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    // 1. Model: język naturalny -> LightCommand
    // GetResponseAsync<T> generuje z typu JSON Schema i wymaga od modelu odpowiedzi w tym formacie.
    var response = await client.GetResponseAsync<LightCommand>(
        [systemPrompt, new ChatMessage(ChatRole.User, input)]);

    if (!response.TryGetResult(out var command))
    {
        Console.WriteLine("[APP] Nie udało się odczytać odpowiedzi modelu.");
        continue;
    }

    Console.WriteLine($"[MODEL] {JsonSerializer.Serialize(command, jsonOptions)}");

    // 2. Aplikacja: walidacja
    // Schemat gwarantuje format danych, ale nie ich sens.
    if (command.DelaySeconds is < 0 or > MaxDelaySeconds)
    {
        Console.WriteLine($"[APP] Nieprawidłowe opóźnienie: {command.DelaySeconds} s (dozwolone 0–{MaxDelaySeconds} s).");
        continue;
    }

    if (command.Action == LightAction.Unknown)
    {
        Console.WriteLine("[APP] Nie rozumiem polecenia. Mogę włączyć lub wyłączyć lampkę albo podać jej stan.");
        continue;
    }

    // 3. Aplikacja: wykonanie
    // O tym, co i kiedy się stanie, decyduje kod C#, a nie model.
    if (command.DelaySeconds > 0)
    {
        Console.WriteLine($"[APP] Czekam {command.DelaySeconds} s...");
        await Task.Delay(TimeSpan.FromSeconds(command.DelaySeconds));
    }

    switch (command.Action)
    {
        case LightAction.TurnOn:
            await SetLight(true);
            break;

        case LightAction.TurnOff:
            await SetLight(false);
            break;

        case LightAction.Status:
            await ShowStatus();
            break;
    }
}

async Task SetLight(bool on)
{
    Console.WriteLine($"[APP] Turning light {(on ? "on" : "off")}...");

    var response = await http.GetAsync($"/rpc/Switch.Set?id=0&on={(on ? "true" : "false")}");
    response.EnsureSuccessStatusCode();

    await ShowStatus();
}

async Task ShowStatus()
{
    var status = await http.GetFromJsonAsync<LightStatus>("/rpc/Switch.GetStatus?id=0");

    Console.WriteLine($"[APP] Light: {(status?.Output == true ? "ON" : "OFF")}, power: {status?.Power} W");
}

// Typ wyniku, na podstawie którego powstaje JSON Schema dla modelu.
// Opisy z [Description] trafiają do schematu i podpowiadają modelowi znaczenie pól.
[Description("Command for the Shelly light")]
public record LightCommand(
    [property: Description("Action requested by the user. Unknown if the request is not about the light or is unclear.")]
    LightAction Action,

    [property: Description("Delay before the action in seconds. 0 if the action should happen immediately.")]
    int DelaySeconds);

public enum LightAction
{
    TurnOn,
    TurnOff,
    Status,
    Unknown
}

public record LightStatus(
    [property: JsonPropertyName("output")] bool Output,
    [property: JsonPropertyName("apower")] double Power
);
