using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OllamaSharp;

if (args.Length == 0)
{
    Console.WriteLine("Usage: Shelly.McpClient <url>");
    return;
}

var shellyUrl = args[0];

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
IChatClient client = new OllamaApiClient(
    new Uri("http://localhost:11434"),
    "qwen2.5:3b");

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