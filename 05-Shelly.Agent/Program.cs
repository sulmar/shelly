using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;
using System.ClientModel;

const string DefaultModel = "qwen3:8b";

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
    Console.WriteLine($"Usage: Shelly.Agent <url> [--model <name>] [--endpoint <url>] (default model: {DefaultModel})");
    return;
}

// Klucz API dla zdalnego endpointu czytamy ze zmiennej środowiskowej, aby nie trafił do historii poleceń.
var apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY");

if (endpoint is not null && string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("Set the LLM_API_KEY environment variable to use --endpoint.");
    return;
}

using var http = new HttpClient
{
    BaseAddress = new Uri(shellyUrl)
};

// Model
// Bez --endpoint używamy lokalnej Ollamy, w przeciwnym razie dowolnego API zgodnego z OpenAI (np. https://llm.hpc.psnc.pl/v1).
IChatClient client = endpoint is null
    ? new OllamaApiClient(
        new Uri("http://localhost:11434"),
        model)
    : new OpenAIClient(
        new ApiKeyCredential(apiKey!),
        new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
        .GetChatClient(model)
        .AsIChatClient();

// Automatyczne wykonywanie narzędzi
IChatClient chatClient = new ChatClientBuilder(client)
    .UseFunctionInvocation()
    .Build();

// Narzędzia
var tools = new[]
{
    AIFunctionFactory.Create(
        TurnOnLight,
        name: "turn_on_light",
        description: "Turns on the Shelly light."),

    AIFunctionFactory.Create(
        TurnOffLight,
        name: "turn_off_light",
        description: "Turns off the Shelly light."),

    AIFunctionFactory.Create(
        GetLightStatus,
        name: "get_light_status",
        description: """
            Gets the current Shelly light status.
            Returns whether the light is on and its power in watts.
            Use this tool to verify the result of an operation.
            """)
};

var options = new ChatOptions
{
    Tools = tools
};

// Historia rozmowy
var messages = new List<ChatMessage>
{
    new(ChatRole.System, """
        Jesteś agentem sterującym lampką Shelly.

        Możesz:
        - włączać lampkę,
        - wyłączać lampkę,
        - odczytywać jej aktualny stan.

        Samodzielnie wybieraj narzędzia potrzebne
        do realizacji polecenia użytkownika.

        Jeśli użytkownik prosi o potwierdzenie wykonania
        operacji, sprawdź aktualny stan urządzenia.

        Jeśli operacja nie przyniosła oczekiwanego rezultatu,
        możesz podjąć próbę naprawy sytuacji.

        Nie twierdź, że operacja zakończyła się sukcesem,
        jeśli nie masz odpowiedniego potwierdzenia.

        Odpowiadaj po polsku.
        """)
};

Console.WriteLine("Shelly Agent");
Console.WriteLine("Type 'exit' to quit.");
Console.WriteLine();

while (true)
{
    Console.Write("> ");

    var input = Console.ReadLine();

    if (input is null ||
        input.Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    messages.Add(new ChatMessage(ChatRole.User, input));

    try
    {
        var response = await chatClient.GetResponseAsync(
            messages,
            options);

        messages.AddMessages(response);

        Console.WriteLine();
        Console.WriteLine($"Assistant: {response.Text}");
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}

// --------------------------------------------
// TOOLS
// --------------------------------------------

async Task<string> TurnOnLight()
{
    Console.WriteLine("[TOOL] TurnOnLight");

    using var response = await http.GetAsync(
        "/rpc/Switch.Set?id=0&on=true");

    response.EnsureSuccessStatusCode();

    return await response.Content.ReadAsStringAsync();
}

async Task<string> TurnOffLight()
{
    Console.WriteLine("[TOOL] TurnOffLight");

    using var response = await http.GetAsync(
        "/rpc/Switch.Set?id=0&on=false");

    response.EnsureSuccessStatusCode();

    return await response.Content.ReadAsStringAsync();
}

async Task<LightStatus> GetLightStatus()
{
    Console.WriteLine("[TOOL] GetLightStatus");

    var status = await http.GetFromJsonAsync<LightStatus>(
        "/rpc/Switch.GetStatus?id=0");

    if (status is null)
    {
        throw new InvalidOperationException(
            "Cannot read Shelly status.");
    }

    Console.WriteLine(
        $"[TOOL RESULT] {JsonSerializer.Serialize(status)}");

    return status;
}

// --------------------------------------------
// MODEL
// --------------------------------------------

public record LightStatus(
    [property: JsonPropertyName("output")] bool Output,
    [property: JsonPropertyName("apower")] double Power,
    [property: JsonPropertyName("voltage")] double Voltage
);