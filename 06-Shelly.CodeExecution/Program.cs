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
    Console.WriteLine($"Usage: Shelly.CodeExecution <url> [--model <name>] [--endpoint <url>] (default model: {DefaultModel})");
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

IChatClient chatClient = new ChatClientBuilder(client)
    .UseFunctionInvocation()
    .Build();

var tools = new[]
{
    AIFunctionFactory.Create(
        GetLightStatus,
        name: "get_light_status",
        description: "Gets current Shelly status, including power in watts."),

    AIFunctionFactory.Create(
        ExecutePython,
        name: "execute_python",
        description: "Executes Python code in an isolated Docker container.")
};

var options = new ChatOptions
{
    Tools = tools
};

var messages = new List<ChatMessage>
{
    new(ChatRole.System, """
                         Jesteś asystentem analizującym zużycie energii lampki Shelly.

                         Masz dostęp do narzędzi:
                         - get_light_status: odczytuje aktualny stan i moc lampki.
                         - execute_python: wykonuje kod Pythona w kontenerze.

                         Korzystaj z narzędzi, gdy są potrzebne do realizacji zadania.

                         Jeśli wykonujesz obliczenia za pomocą Pythona:
                         - generuj kompletny kod,
                         - korzystaj z biblioteki standardowej,
                         - wypisuj wyniki przez print().

                         Nie twierdź, że wykonałeś operację,
                         jeśli narzędzie jej nie potwierdziło.

                         Odpowiadaj po polsku.
                         """)
};

Console.WriteLine("Shelly Code Execution");
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

async Task<LightStatus> GetLightStatus()
{
    Console.WriteLine("[TOOL] GetLightStatus");

    var status = await http.GetFromJsonAsync<LightStatus>(
        "/rpc/Switch.GetStatus?id=0");

    if (status is null)
        throw new InvalidOperationException("Cannot read Shelly status.");
    
    Console.WriteLine($"[TOOL RESULT] {JsonSerializer.Serialize(status)}");

    return status;
}

async Task<string> ExecutePython(
    [Description("Complete Python source code")] string code)
{
    Console.WriteLine("[TOOL] ExecutePython");
    Console.WriteLine();
    Console.WriteLine("Generated Python:");
    Console.WriteLine(code);
    Console.WriteLine();

    var result = await PythonExecutor.ExecuteAsync(code);

    Console.WriteLine($"Execution result:\n{result}");

    return result;
}

public record LightStatus(
    [property: JsonPropertyName("output")]
    bool Output,

    [property: JsonPropertyName("apower")]
    double Power,

    [property: JsonPropertyName("voltage")]
    double Voltage
);