using System.Net.Http.Json;
using System.Text.Json.Serialization;

var shellyUrl = args.FirstOrDefault()
                ?? throw new ArgumentException("Usage: Shelly.Http <url>");

using var http = new HttpClient
{
    BaseAddress = new Uri(shellyUrl)
};

Console.WriteLine("Turning light ON...");
await http.GetAsync("/rpc/Switch.Set?id=0&on=true");

await ShowStatus();

await Task.Delay(2000);

Console.WriteLine("Turning light OFF...");
await http.GetAsync("/rpc/Switch.Set?id=0&on=false");

await ShowStatus();

async Task ShowStatus()
{
    var status = await http.GetFromJsonAsync<LightStatus>(
        "/rpc/Switch.GetStatus?id=0");

    Console.WriteLine($"Light: {(status?.Output == true ? "ON" : "OFF")}");
    Console.WriteLine($"Power: {status?.Power} W");
    Console.WriteLine($"Temperature: {status?.Temperature?.Celsius} °C");
    Console.WriteLine();
}

public record LightStatus(
    [property: JsonPropertyName("output")] bool Output,
    [property: JsonPropertyName("apower")] double Power,
    [property: JsonPropertyName("temperature")] DeviceTemperature? Temperature
);

public record DeviceTemperature(
    [property: JsonPropertyName("tC")] double Celsius
);