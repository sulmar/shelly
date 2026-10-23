using System.ClientModel;
using System.ComponentModel;
using A2A;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

const string DefaultModel = "qwen3:8b";
const string DefaultAgentUrl = "http://localhost:5090";

string? endpoint = null;
var model = DefaultModel;
var agentUrl = DefaultAgentUrl;

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
        agentUrl = args[i];
}

// Klucz API dla zdalnego endpointu czytamy ze zmiennej środowiskowej, aby nie trafił do historii poleceń.
var apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY");

if (endpoint is not null && string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("Set the LLM_API_KEY environment variable to use --endpoint.");
    return;
}

// A2A: odkrywanie agenta
// Klient zna tylko adres. Wszystko inne (nazwę, opis, umiejętności, endpoint) odczytuje z Agent Card.
var resolver = new A2ACardResolver(new Uri(agentUrl));
var agentCard = await resolver.GetAgentCardAsync();

Console.WriteLine($"[A2A] Discovered agent: {agentCard.Name} — {agentCard.Description}");

foreach (var skill in agentCard.Skills ?? [])
{
    Console.WriteLine($"[A2A]   skill: {skill.Name} — {skill.Description}");
}

// Zdalny agent jest widoczny jako zwykły AIAgent, tak samo jak agent lokalny.
AIAgent shellyAgent = agentCard.AsAIAgent();

// Sesja zachowuje kontekst rozmowy ze zdalnym agentem między kolejnymi wywołaniami.
var shellySession = await shellyAgent.CreateSessionAsync();

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

// Agent-koordynator
// Nie ma dostępu do urządzenia. Sterowanie lampką deleguje do zdalnego agenta przez A2A,
// a obliczenia kosztów energii wykonuje sam.
AIAgent assistant = client.AsAIAgent(
    name: "home-assistant",
    instructions: $"""
        Jesteś asystentem domowym.

        Nie masz bezpośredniego dostępu do urządzeń.
        Sprawy związane z lampką (włączanie, wyłączanie, stan, pobór mocy)
        przekazuj agentowi {agentCard.Name} narzędziem ask_shelly_agent.
        Formułuj dla niego krótkie, konkretne polecenia po polsku.

        Koszty energii obliczaj narzędziem calculate_energy_cost,
        korzystając z mocy podanej przez agenta {agentCard.Name}.

        Nie twierdź, że operacja została wykonana,
        jeśli agent {agentCard.Name} tego nie potwierdził.

        Odpowiadaj po polsku.
        """,
    tools:
    [
        // Opis narzędzia pochodzi z Agent Card, więc model wie, w czym zdalny agent może pomóc.
        AIFunctionFactory.Create(
            AskShellyAgent,
            name: "ask_shelly_agent",
            description: $"Sends a request to the remote agent '{agentCard.Name}': {agentCard.Description}"),

        AIFunctionFactory.Create(
            CalculateEnergyCost,
            name: "calculate_energy_cost",
            description: "Calculates energy usage (kWh) and cost for a device with the given power, time and price.")
    ]);

var session = await assistant.CreateSessionAsync();

Console.WriteLine();
Console.WriteLine("Home Assistant (A2A client)");
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

    try
    {
        var response = await assistant.RunAsync(input, session);

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

async Task<string> AskShellyAgent(
    [Description("Request for the remote agent, in Polish")] string request)
{
    Console.WriteLine($"[A2A] -> {agentCard.Name}: {request}");

    var response = await shellyAgent.RunAsync(request, shellySession);

    Console.WriteLine($"[A2A] <- {agentCard.Name}: {response.Text}");

    return response.Text;
}

string CalculateEnergyCost(
    [Description("Device power in watts")] double powerWatts,
    [Description("Operating time in hours")] double hours,
    [Description("Energy price per kWh")] double pricePerKwh)
{
    var energyKwh = powerWatts * hours / 1000;
    var cost = energyKwh * pricePerKwh;

    Console.WriteLine($"[TOOL] CalculateEnergyCost: {powerWatts} W × {hours} h × {pricePerKwh}/kWh = {cost:0.00}");

    return $"Energy: {energyKwh:0.###} kWh, cost: {cost:0.00}";
}
