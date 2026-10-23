using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;
using System.ClientModel;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

const string DefaultModel = "qwen3:8b";
const string ServiceName = "Shelly.Observability";

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
    Console.WriteLine($"Usage: Shelly.Observability <url> [--model <name>] [--endpoint <url>] (default model: {DefaultModel})");
    return;
}

// Klucz API dla zdalnego endpointu czytamy ze zmiennej środowiskowej, aby nie trafił do historii poleceń.
var apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY");

if (endpoint is not null && string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("Set the LLM_API_KEY environment variable to use --endpoint.");
    return;
}

// OpenTelemetry
// Ślady i metryki wysyłamy protokołem OTLP, domyślnie na http://localhost:4317 (np. Aspire Dashboard).
// Adres można zmienić zmienną środowiskową OTEL_EXPORTER_OTLP_ENDPOINT.
var resource = ResourceBuilder.CreateDefault().AddService(ServiceName);

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(resource)
    .AddSource(ServiceName)          // Spany aplikacji i Microsoft.Extensions.AI
    .AddHttpClientInstrumentation()  // Żądania HTTP do Ollamy i do Shelly
    .AddOtlpExporter()
    .Build();

using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .SetResourceBuilder(resource)
    .AddMeter(ServiceName)           // Metryki Microsoft.Extensions.AI, m.in. zużycie tokenów
    .AddOtlpExporter()
    .Build();

using var activitySource = new ActivitySource(ServiceName);

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

// Automatyczne wykonywanie narzędzi i telemetria.
// UseFunctionInvocation jest na zewnątrz, więc każde wywołanie modelu i każde narzędzie dostaje osobny span.
IChatClient chatClient = new ChatClientBuilder(client)
    .UseFunctionInvocation()
    .UseOpenTelemetry(
        sourceName: ServiceName,
        // Treść promptów i odpowiedzi trafia do śladów. Wygodne do nauki, ale w produkcji to dane wrażliwe.
        configure: telemetry => telemetry.EnableSensitiveData = true)
    .Build();

// Narzędzia
// Odczyt stanu jest bezpieczny i wykonuje się automatycznie.
// Zmiana stanu urządzenia wymaga zatwierdzenia przez człowieka.
var tools = new AITool[]
{
    new ApprovalRequiredAIFunction(
        AIFunctionFactory.Create(
            TurnOnLight,
            name: "turn_on_light",
            description: "Turns on the Shelly light.")),

    new ApprovalRequiredAIFunction(
        AIFunctionFactory.Create(
            TurnOffLight,
            name: "turn_off_light",
            description: "Turns off the Shelly light.")),

    AIFunctionFactory.Create(
        GetLightStatus,
        name: "get_light_status",
        description: """
            Gets the current Shelly light status.
            Returns whether the light is on and its power in watts.
            Use this tool to verify the result of an operation.
            """),

    AIFunctionFactory.Create(
        GetLightConfig,
        name: "get_light_config",
        description: "Gets the Shelly light configuration, including its name.")
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
        - odczytywać jej aktualny stan i konfigurację.

        Samodzielnie wybieraj narzędzia potrzebne
        do realizacji polecenia użytkownika.

        Włączenie i wyłączenie lampki wymaga zgody użytkownika.
        O zgodę pyta aplikacja w momencie wywołania narzędzia,
        więc nie pytaj o nią sam — po prostu wywołaj narzędzie.
        Jeśli użytkownik odrzuci operację, nie ponawiaj jej
        i poinformuj użytkownika, że nie została wykonana.

        Nie twierdź, że operacja zakończyła się sukcesem,
        jeśli nie masz odpowiedniego potwierdzenia.

        Odpowiadaj po polsku.
        """)
};

Console.WriteLine("Shelly Observability");
Console.WriteLine("Traces and metrics: OTLP -> Aspire Dashboard (http://localhost:18888)");
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

    // Jedno polecenie użytkownika = jeden ślad, w którym zagnieżdżone są wywołania modelu, narzędzi i HTTP.
    using var turn = activitySource.StartActivity("user_turn");
    turn?.SetTag("user.input", input);

    try
    {
        var response = await chatClient.GetResponseAsync(
            messages,
            options);

        messages.AddMessages(response);

        // Zamiast wykonać narzędzie wymagające zgody, model zwraca prośbę o zatwierdzenie.
        // Dopóki takie prośby się pojawiają, pytamy użytkownika i przekazujemy decyzję z powrotem.
        var requests = GetApprovalRequests(response);

        while (requests.Count > 0)
        {
            var approvals = requests
                .Select(request => (AIContent)request.CreateResponse(
                    approved: AskForApproval(request),
                    reason: "Decyzja użytkownika."))
                .ToList();

            messages.Add(new ChatMessage(ChatRole.User, approvals));

            // UseFunctionInvocation wykona zatwierdzone narzędzia,
            // a modelowi przekaże informację o odrzuconych.
            response = await chatClient.GetResponseAsync(
                messages,
                options);

            messages.AddMessages(response);

            requests = GetApprovalRequests(response);
        }

        turn?.SetTag("assistant.response", response.Text);

        Console.WriteLine();
        Console.WriteLine($"Assistant: {response.Text}");
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        turn?.SetStatus(ActivityStatusCode.Error, ex.Message);

        Console.WriteLine($"Error: {ex.Message}");
    }
}

// --------------------------------------------
// HUMAN-IN-THE-LOOP
// --------------------------------------------

static List<ToolApprovalRequestContent> GetApprovalRequests(ChatResponse response) =>
    response.Messages
        .SelectMany(message => message.Contents)
        .OfType<ToolApprovalRequestContent>()
        .ToList();

bool AskForApproval(ToolApprovalRequestContent request)
{
    var toolName = request.ToolCall is FunctionCallContent call
        ? call.Name
        : request.ToolCall.CallId;

    // Czas oczekiwania na decyzję człowieka będzie widoczny na osi czasu śladu.
    using var approval = activitySource.StartActivity("user_approval");
    approval?.SetTag("gen_ai.tool.name", toolName);

    Console.WriteLine();
    Console.WriteLine($"[APPROVAL] Agent chce wywołać narzędzie: {toolName}");
    Console.Write("[APPROVAL] Zezwolić? [t/N] ");

    // Domyślnie odmawiamy: brak jednoznacznej zgody oznacza brak zgody.
    var answer = Console.ReadLine()?.Trim();
    var approved = answer is not null &&
                   (answer.Equals("t", StringComparison.OrdinalIgnoreCase) ||
                    answer.Equals("tak", StringComparison.OrdinalIgnoreCase));

    approval?.SetTag("approval.approved", approved);

    Console.WriteLine(approved
        ? "[APPROVAL] Zatwierdzono."
        : "[APPROVAL] Odrzucono.");

    return approved;
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

async Task<LightConfig> GetLightConfig()
{
    Console.WriteLine("[TOOL] GetLightConfig");

    var config = await http.GetFromJsonAsync<LightConfig>(
        "/rpc/Switch.GetConfig?id=0");

    if (config is null)
    {
        throw new InvalidOperationException(
            "Cannot read Shelly config.");
    }

    Console.WriteLine(
        $"[TOOL RESULT] {JsonSerializer.Serialize(config)}");

    return config;
}

// --------------------------------------------
// MODEL
// --------------------------------------------

public record LightStatus(
    [property: JsonPropertyName("output")] bool Output,
    [property: JsonPropertyName("apower")] double Power,
    [property: JsonPropertyName("voltage")] double Voltage
);

public record LightConfig(
    [property: JsonPropertyName("name")] string? Name
);
