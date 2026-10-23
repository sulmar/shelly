using System.ClientModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

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
    Console.WriteLine($"Usage: Shelly.Skills <url> [--model <name>] [--endpoint <url>] (default model: {DefaultModel})");
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
// Raport z obliczeniami dla dwóch taryf to długa odpowiedź, a lokalny model bywa wolny,
// dlatego wydłużamy domyślny limit czasu HttpClient (100 s).
IChatClient client = endpoint is null
    ? new OllamaApiClient(
        new HttpClient
        {
            BaseAddress = new Uri("http://localhost:11434"),
            Timeout = TimeSpan.FromMinutes(5)
        },
        model)
    : new OpenAIClient(
        new ApiKeyCredential(apiKey!),
        new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
        .GetChatClient(model)
        .AsIChatClient();

// Skills
// Każdy podkatalog skills/ z plikiem SKILL.md to jeden skill.
// Do promptu trafiają tylko nazwy i opisy skilli. Pełną treść model ładuje dopiero wtedy, gdy jej potrzebuje.
var skillsPath = Path.Combine(AppContext.BaseDirectory, "skills");

var skillsProvider = new AgentSkillsProvider(
    skillsPath,
    // Nasze skille nie zawierają skryptów, więc nie potrzebujemy mechanizmu ich uruchamiania.
    null,
    null,
    new AgentSkillsProviderOptions
    {
        // Domyślnie załadowanie skilla i odczyt jego zasobów wymagają zgody użytkownika,
        // bo treść skilla trafia do kontekstu modelu. Nasze skille są lokalne i zaufane.
        DisableLoadSkillApproval = true,
        DisableReadSkillResourceApproval = true,

        // Narzędzie run_skill_script jest zawsze dostępne dla modelu. Nasze skille nie mają skryptów,
        // więc wywołanie może tylko zwrócić błąd — model dowie się, że skryptu nie ma, i policzy sam.
        // Gdy skille zawierają skrypty, zatwierdzanie ich uruchamiania warto zostawić włączone.
        DisableRunSkillScriptApproval = true
    });

// Agent
AIAgent agent = client
    .AsAIAgent(new ChatClientAgentOptions
    {
        Name = "shelly-agent",
        ChatOptions = new ChatOptions
        {
            Instructions = """
                Jesteś agentem sterującym lampką Shelly.

                Możesz włączać i wyłączać lampkę oraz odczytywać jej stan.
                Jeśli zadanie pasuje do opisu jednego z dostępnych skilli,
                najpierw go załaduj i postępuj zgodnie z jego instrukcjami.

                Nie twierdź, że operacja zakończyła się sukcesem,
                jeśli nie masz odpowiedniego potwierdzenia.

                Odpowiadaj po polsku.
                """,
            Tools =
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
            ]
        },
        AIContextProviders = [skillsProvider]
    })
    .AsBuilder()
    // Middleware wywołań funkcji: pokazuje, kiedy model ładuje skill lub czyta jego zasób.
    .Use(async (agent, context, next, cancellationToken) =>
    {
        var name = context.Function.Name;

        if (name is AgentSkillsProvider.LoadSkillToolName or AgentSkillsProvider.ReadSkillResourceToolName)
        {
            Console.WriteLine($"[SKILL] {name} {JsonSerializer.Serialize(context.Arguments)}");
        }

        return await next(context, cancellationToken);
    })
    .Build();

var session = await agent.CreateSessionAsync();

Console.WriteLine("Shelly Skills");
Console.WriteLine($"Skills: {string.Join(", ", Directory.GetDirectories(skillsPath).Select(Path.GetFileName))}");
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
        var response = await agent.RunAsync(input, session);

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
