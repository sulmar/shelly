using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
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
    Console.WriteLine($"Usage: Shelly.McpClient <url> [--model <name>] [--endpoint <url>] (default model: {DefaultModel})");
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

// MCP client
await using var mcpClient = await McpClient.CreateAsync(
    new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "Shelly",
        Command = "dotnet",
        Arguments =
        [
            "run",
            "--project",
            "03-Shelly.Mcp",
            "--",
            shellyUrl
        ]
    }));

// Discover tools
var tools = await mcpClient.ListToolsAsync();

foreach (var tool in tools)
{
    Console.WriteLine($"[MCP] Discovered tool: {tool.Name}");
}

// LLM
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

// Automatic tool calling
IChatClient chatClient = new ChatClientBuilder(client)
    .UseFunctionInvocation()
    .Build();

var options = new ChatOptions
{
    Tools = [.. tools]
};

var messages = new List<ChatMessage>
{
    new(ChatRole.System, """
                         Jesteś asystentem sterującym oświetleniem.
                         Odpowiadaj po polsku, krótko i naturalnie.
                         Nie twierdź, że wykonałeś operację, jeśli narzędzie jej nie potwierdziło.
                         """)
};

Console.WriteLine("Enter a command (or 'exit' to quit):");

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

    var response = await chatClient.GetResponseAsync(messages, options);

    messages.AddMessages(response);

    Console.WriteLine($"Assistant: {response.Text}");
}