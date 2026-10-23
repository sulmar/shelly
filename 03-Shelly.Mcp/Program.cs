using System.ComponentModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var shellyUrl = args.FirstOrDefault()
                ?? throw new ArgumentException("Usage: Shelly.Mcp <url>");

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(new HttpClient
{
    BaseAddress = new Uri(shellyUrl)
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    .WithPromptsFromAssembly();

await builder.Build().RunAsync();

[McpServerToolType]
public static class ShellyTools
{
    [McpServerTool]
    [Description("Turns on the light")]
    public static async Task<string> TurnOnLight(HttpClient http)
    {
        var response = await http.GetAsync("/rpc/Switch.Set?id=0&on=true");
        response.EnsureSuccessStatusCode();

        return "Light turned on.";
    }

    [McpServerTool]
    [Description("Turns off the light")]
    public static async Task<string> TurnOffLight(HttpClient http)
    {
        var response = await http.GetAsync("/rpc/Switch.Set?id=0&on=false");
        response.EnsureSuccessStatusCode();

        return "Light turned off.";
    }
}

// Resources: dane, które klient MCP może odczytać i dołączyć do kontekstu.
// W przeciwieństwie do narzędzi nie wykonują działań i nie zmieniają stanu urządzenia.
[McpServerResourceType]
public static class ShellyResources
{
    [McpServerResource(UriTemplate = "shelly://status", Name = "status", MimeType = "application/json")]
    [Description("Current Shelly light status: on/off state, power in watts, voltage, current, energy and temperature.")]
    public static async Task<string> Status(HttpClient http)
    {
        return await http.GetStringAsync("/rpc/Switch.GetStatus?id=0");
    }

    [McpServerResource(UriTemplate = "shelly://config", Name = "config", MimeType = "application/json")]
    [Description("Shelly light configuration, including its name.")]
    public static async Task<string> Config(HttpClient http)
    {
        return await http.GetStringAsync("/rpc/Switch.GetConfig?id=0");
    }
}

// Prompts: gotowe szablony poleceń, które użytkownik wybiera w kliencie MCP.
// Serwer zwraca wiadomości, a decyzję o ich wysłaniu do modelu podejmuje użytkownik.
[McpServerPromptType]
public static class ShellyPrompts
{
    [McpServerPrompt(Name = "ensure_light_state")]
    [Description("Makes sure the light is in the requested state.")]
    public static async Task<string> EnsureLightState(
        HttpClient http,
        [Description("Requested state: on or off")] string state)
    {
        var target = state.Equals("off", StringComparison.OrdinalIgnoreCase) ? "wyłączona" : "włączona";

        // Zasoby udostępnia klient, a nie model, dlatego aktualny stan dołączamy do treści promptu.
        var status = await http.GetStringAsync("/rpc/Switch.GetStatus?id=0");

        return $"""
            Upewnij się, że lampka jest {target}.

            Aktualny stan lampki Shelly (JSON, pole output = true oznacza, że lampka jest włączona):
            {status}

            Jeśli lampka nie jest {target}, użyj odpowiedniego narzędzia, aby to zmienić.
            Jeśli lampka już jest {target}, nie zmieniaj jej stanu.
            Na koniec krótko poinformuj, co zostało zrobione.
            """;
    }

    [McpServerPrompt(Name = "energy_report")]
    [Description("Prepares an energy usage and cost report for the light based on its current power.")]
    public static async Task<IEnumerable<ChatMessage>> EnergyReport(
        HttpClient http,
        [Description("Number of hours the light is on")] double hours,
        [Description("Energy price per kWh, e.g. 1.20")] double pricePerKwh)
    {
        // Prompt może od razu dołączyć aktualne dane z urządzenia.
        var status = await http.GetStringAsync("/rpc/Switch.GetStatus?id=0");

        return
        [
            new ChatMessage(ChatRole.User, $"""
                Aktualny stan lampki Shelly (JSON):
                {status}

                Pole apower to chwilowy pobór mocy w watach.
                """),

            new ChatMessage(ChatRole.User, $"""
                Przygotuj krótki raport zużycia energii dla tej lampki:
                - zużycie energii w kWh przy {hours} godzinach świecenia,
                - koszt przy cenie {pricePerKwh} zł/kWh,
                - jeśli lampka jest wyłączona (apower = 0), napisz, że do obliczeń
                  potrzebny jest pomiar mocy po jej włączeniu.

                Pokaż obliczenia. Odpowiedz po polsku.
                """)
        ];
    }
}
