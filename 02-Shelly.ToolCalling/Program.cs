using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;
using OpenAI;
using System.ClientModel;

const string DefaultModel = "qwen2.5:3b";

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
    Console.WriteLine($"Usage: Shelly.ToolCalling <url> [--model <name>] [--endpoint <url>] (default model: {DefaultModel})");
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

async Task TurnOnLight()
{
    Console.WriteLine("[Tool] Turning light on...");

    var response = await http.GetAsync("/rpc/Switch.Set?id=0&on=true");
    response.EnsureSuccessStatusCode();
}

async Task TurnOffLight()
{
    Console.WriteLine("[Tool] Turning light off...");

    var response = await http.GetAsync("/rpc/Switch.Set?id=0&on=false");
    response.EnsureSuccessStatusCode();
}

// Sprawdzenie czy Ollama działa
// IsRunningAsync wywołuje endpoint Ollamy GET / (odpowiada "Ollama is running").
// Gdy serwer nie nasłuchuje, rzuca HttpRequestException zamiast zwrócić false, dlatego łapiemy wyjątek.
static async Task<bool> OllamaIsRunningAsync(OllamaApiClient ollama, CancellationToken cancellationToken = default)
{
    try
    {
        return await ollama.IsRunningAsync(cancellationToken);
    }
    catch (HttpRequestException)
    {
        return false;
    }
}

// Pobranie informacji o modelu lub null, gdy model nie jest pobrany lokalnie
// ShowModelAsync wywołuje endpoint Ollamy POST /api/show (to samo co `ollama show`), który dla nieznanego modelu zwraca 404.
static async Task<ShowModelResponse?> GetModelInfoAsync(OllamaApiClient ollama, string model, CancellationToken cancellationToken = default)
{
    try
    {
        return await ollama.ShowModelAsync(model, cancellationToken);
    }
    catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return null;
    }
}

// Pobranie modelu z rejestru Ollamy z raportowaniem postępu przez IProgress<T>
// PullModelAsync wywołuje endpoint Ollamy POST /api/pull (to samo co `ollama pull`), który strumieniuje kolejne etapy pobierania.
static async Task<bool> PullModelAsync(OllamaApiClient ollama, string model, IProgress<PullModelResponse>? progress = null, CancellationToken cancellationToken = default)
{
    try
    {
        await foreach (var response in ollama.PullModelAsync(model, cancellationToken))
        {
            if (response is not null)
                progress?.Report(response);
        }

        return true;
    }
    catch (Exception e) when (e is HttpRequestException or OllamaSharp.Models.Exceptions.ResponseError)
    {
        Console.WriteLine($"Failed to download model {model}: {e.Message}");
        return false;
    }
}

// Tools
// Udostępniamy modelowi dwie funkcje wraz z opisami.
var tools = new[]
{
    AIFunctionFactory.Create(
        TurnOnLight,
        name: "turn_on_light",
        description: "Turns on the light"),

    AIFunctionFactory.Create(
        TurnOffLight,
        name: "turn_off_light",
        description: "Turns off the light")
};

// LLM
// Bez --endpoint używamy lokalnej Ollamy, w przeciwnym razie dowolnego API zgodnego z OpenAI (np. https://llm.hpc.psnc.pl/v1).
IChatClient client;

if (endpoint is null)
{
    var ollama = new OllamaApiClient(new Uri("http://localhost:11434"), model);

    if (!await OllamaIsRunningAsync(ollama))
    {
        Console.WriteLine($"Ollama is not running at {ollama.Uri}. Run: ollama serve");
        return;
    }

    // GetVersionAsync wywołuje endpoint Ollamy GET /api/version (to samo co `ollama --version`).
    var version = await ollama.GetVersionAsync();

    Console.WriteLine($"Ollama version: {version}");

    var modelInfo = await GetModelInfoAsync(ollama, model);

    if (modelInfo is null)
    {
        Console.WriteLine($"Model {model} not found.");

        // ListLocalModelsAsync wywołuje endpoint Ollamy GET /api/tags (to samo co `ollama list`).
        var models = await ollama.ListLocalModelsAsync();

        Console.WriteLine("Available models:");

        foreach (var m in models)
            Console.WriteLine($"  {m.Name}");

        Console.Write($"Download model {model}? [y/N] ");

        if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            return;

        if (!await PullModelAsync(ollama, model, new OllamaPullProgress()))
            return;

        modelInfo = await GetModelInfoAsync(ollama, model);

        if (modelInfo is null)
        {
            Console.WriteLine($"Model {model} is still not available.");
            return;
        }
    }

    // Capabilities pochodzą z odpowiedzi /api/show, np. completion, tools, vision.
    // Bez "tools" model nie potrafi wywoływać funkcji, więc Tool Calling nie zadziała.
    if (modelInfo.Capabilities is { } capabilities && !capabilities.Contains("tools"))
    {
        Console.WriteLine($"Model {model} does not support tools (capabilities: {string.Join(", ", capabilities)}).");
        return;
    }

    // ListRunningModelsAsync wywołuje endpoint Ollamy GET /api/ps (to samo co `ollama ps`).
    // Model niezaładowany do pamięci zostanie wczytany przy pierwszym pytaniu, więc pierwsza odpowiedź potrwa dłużej.
    var runningModels = await ollama.ListRunningModelsAsync();

    var running = runningModels.FirstOrDefault(m => string.Equals(m.Name, model, StringComparison.OrdinalIgnoreCase));

    Console.WriteLine(running is null
        ? $"Model {model} is not loaded yet, the first response may be slower."
        : $"Model {model} is loaded (expires at {running.ExpiresAt:HH:mm:ss}).");

    client = ollama;
}
else
{
    client = new OpenAIClient(
            new ApiKeyCredential(apiKey!),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
        .GetChatClient(model)
        .AsIChatClient();
}

// Automatic tool calling
IChatClient chatClient = new ChatClientBuilder(client)
    .UseFunctionInvocation() // Automatycznie obsługuje cykl Tool Calling: model wybiera narzędzie, aplikacja wykonuje funkcję i przekazuje wynik modelowi.
    .Build();

var options = new ChatOptions
{
    Tools = tools
};

Console.WriteLine("Enter a command (or 'exit' to quit):");

while (true)
{
    Console.Write("> ");

    var input = Console.ReadLine();

    if (input is null || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    var response = await chatClient.GetResponseAsync(input, options);

    Console.WriteLine($"Assistant: {response.Text}");
}
