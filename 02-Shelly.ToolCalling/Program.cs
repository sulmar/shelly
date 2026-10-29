using Microsoft.Extensions.AI;
using OllamaSharp;
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

// Sprawdzenie czy model jest pobrany lokalnie
// ShowModelAsync wywołuje endpoint Ollamy POST /api/show (to samo co `ollama show`), który dla nieznanego modelu zwraca 404.
static async Task<bool> ModelExistsAsync(OllamaApiClient ollama, string model, CancellationToken cancellationToken = default)
{
    try
    {
        await ollama.ShowModelAsync(model, cancellationToken);
        return true;
    }
    catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
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

    if (!await ModelExistsAsync(ollama, model))
    {
        Console.WriteLine($"Model {model} not found. Run: ollama pull {model}");

        // ListLocalModelsAsync wywołuje endpoint Ollamy GET /api/tags (to samo co `ollama list`).
        var models = await ollama.ListLocalModelsAsync();

        Console.WriteLine("Available models:");

        foreach (var m in models)
            Console.WriteLine($"  {m.Name}");

        return;
    }

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
