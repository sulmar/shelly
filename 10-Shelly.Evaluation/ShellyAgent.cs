using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace Shelly.Evaluation;

// Agent z 05-Shelly.Agent: te same narzędzia i ten sam prompt systemowy,
// wydzielone do klasy, aby test mógł go uruchomić i ocenić.
public sealed class ShellyAgent
{
    public const string SystemPrompt = """
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
        """;

    private readonly IChatClient chatClient;
    private readonly HttpClient http;

    public ShellyAgent(IChatClient client, HttpClient http)
    {
        this.http = http;

        chatClient = new ChatClientBuilder(client)
            .UseFunctionInvocation()
            .Build();

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
                description: """
                    Gets the current Shelly light status.
                    Returns whether the light is on and its power in watts.
                    Use this tool to verify the result of an operation.
                    """)
        ];
    }

    public IReadOnlyList<AITool> Tools { get; }

    // Zwraca rozmowę (prompt systemowy + polecenie) oraz odpowiedź agenta,
    // która zawiera również wywołania narzędzi i ich wyniki.
    public async Task<(List<ChatMessage> Messages, ChatResponse Response)> RunAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, input)
        };

        var response = await chatClient.GetResponseAsync(
            messages,
            new ChatOptions { Tools = [.. Tools] },
            cancellationToken);

        return (messages, response);
    }

    private async Task<string> TurnOnLight()
    {
        using var response = await http.GetAsync("/rpc/Switch.Set?id=0&on=true");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    private async Task<string> TurnOffLight()
    {
        using var response = await http.GetAsync("/rpc/Switch.Set?id=0&on=false");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    private async Task<LightStatus> GetLightStatus()
    {
        return await http.GetFromJsonAsync<LightStatus>("/rpc/Switch.GetStatus?id=0")
               ?? throw new InvalidOperationException("Cannot read Shelly status.");
    }
}

public record LightStatus(
    [property: JsonPropertyName("output")] bool Output,
    [property: JsonPropertyName("apower")] double Power,
    [property: JsonPropertyName("voltage")] double Voltage
);
