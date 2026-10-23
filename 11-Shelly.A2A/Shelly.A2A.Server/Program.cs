using System.ClientModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using A2A;
using A2A.AspNetCore;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

const string DefaultModel = "qwen3:8b";
const string AgentUrl = "http://localhost:5090";

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
    Console.WriteLine($"Usage: Shelly.A2A.Server <url> [--model <name>] [--endpoint <url>] (default model: {DefaultModel})");
    return;
}

// Klucz API dla zdalnego endpointu czytamy ze zmiennej środowiskowej, aby nie trafił do historii poleceń.
var apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY");

if (endpoint is not null && string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("Set the LLM_API_KEY environment variable to use --endpoint.");
    return;
}

var builder = WebApplication.CreateBuilder();

builder.WebHost.UseUrls(AgentUrl);

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

// Agent
// W Microsoft Agent Framework agent to obiekt AIAgent: model + instrukcje + narzędzia.
// Pętlę Tool Calling (UseFunctionInvocation z poprzednich przykładów) framework obsługuje sam.
AIAgent agent = client.AsAIAgent(
    name: "shelly-operator",
    description: "Steruje lampką Shelly: włącza, wyłącza i odczytuje jej stan oraz pobór mocy.",
    instructions: """
        Jesteś agentem sterującym lampką Shelly.

        Możesz włączać i wyłączać lampkę oraz odczytywać jej stan i pobór mocy.
        Samodzielnie wybieraj narzędzia potrzebne do realizacji polecenia.
        Po zmianie stanu sprawdź aktualny stan urządzenia.

        Nie twierdź, że operacja zakończyła się sukcesem,
        jeśli nie masz odpowiedniego potwierdzenia.

        Odpowiadaj krótko, po polsku, podając stan lampki i moc w watach.
        """,
    tools:
    [
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
            description: "Gets the current Shelly light status, including power in watts.")
    ]);

// A2A
// Agent Card opisuje agenta innym agentom: kim jest, co potrafi i gdzie go znaleźć.
// Jest publikowana pod adresem /.well-known/agent-card.json.
var agentCard = new AgentCard
{
    Name = agent.Name!,
    Description = agent.Description!,
    Version = "1.0.0",
    DefaultInputModes = ["text/plain"],
    DefaultOutputModes = ["text/plain"],
    Capabilities = new AgentCapabilities { Streaming = false },
    SupportedInterfaces =
    [
        new AgentInterface
        {
            Url = $"{AgentUrl}/a2a",
            ProtocolBinding = ProtocolBindingNames.HttpJson
        }
    ],
    Skills =
    [
        new A2A.AgentSkill
        {
            Id = "light-control",
            Name = "Sterowanie lampką",
            Description = "Włącza i wyłącza lampkę Shelly.",
            Tags = ["shelly", "light", "iot"],
            Examples = ["Włącz lampkę.", "Wyłącz lampkę."]
        },
        new A2A.AgentSkill
        {
            Id = "light-status",
            Name = "Stan lampki",
            Description = "Odczytuje stan lampki Shelly i jej chwilowy pobór mocy w watach.",
            Tags = ["shelly", "power", "status"],
            Examples = ["Czy lampka jest włączona?", "Ile mocy pobiera lampka?"]
        }
    ]
};

builder.AddA2AServer(agent);

var app = builder.Build();

app.MapA2AHttpJson(agent, "/a2a");
app.MapWellKnownAgentCard(agentCard);

Console.WriteLine($"Shelly A2A agent '{agent.Name}' running at {AgentUrl}");
Console.WriteLine($"Agent Card: {AgentUrl}/.well-known/agent-card.json");
Console.WriteLine($"Connected to Shelly at {shellyUrl}, model {model}");

app.Run();

// --------------------------------------------
// TOOLS
// --------------------------------------------

async Task<string> TurnOnLight()
{
    Console.WriteLine("[TOOL] TurnOnLight");

    using var response = await http.GetAsync("/rpc/Switch.Set?id=0&on=true");
    response.EnsureSuccessStatusCode();

    return await response.Content.ReadAsStringAsync();
}

async Task<string> TurnOffLight()
{
    Console.WriteLine("[TOOL] TurnOffLight");

    using var response = await http.GetAsync("/rpc/Switch.Set?id=0&on=false");
    response.EnsureSuccessStatusCode();

    return await response.Content.ReadAsStringAsync();
}

async Task<LightStatus> GetLightStatus()
{
    Console.WriteLine("[TOOL] GetLightStatus");

    var status = await http.GetFromJsonAsync<LightStatus>("/rpc/Switch.GetStatus?id=0")
                 ?? throw new InvalidOperationException("Cannot read Shelly status.");

    Console.WriteLine($"[TOOL RESULT] {JsonSerializer.Serialize(status)}");

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
