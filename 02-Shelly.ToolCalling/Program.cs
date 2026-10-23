using Microsoft.Extensions.AI;
using OllamaSharp;

const string DefaultModel = "qwen2.5:3b";

string? shellyUrl = null;
var model = DefaultModel;

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--model" && i + 1 < args.Length)
        model = args[++i];
    else if (args[i].StartsWith("--model="))
        model = args[i]["--model=".Length..];
    else
        shellyUrl ??= args[i];
}

if (shellyUrl is null)
{
    Console.WriteLine($"Usage: Shelly.ToolCalling <url> [--model <name>] (default model: {DefaultModel})");
    return;
}

Console.WriteLine($"Connecting to Shelly at {shellyUrl}");
Console.WriteLine($"Using model {model}");

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
IChatClient client = new OllamaApiClient(
    new Uri("http://localhost:11434"),
    model);

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